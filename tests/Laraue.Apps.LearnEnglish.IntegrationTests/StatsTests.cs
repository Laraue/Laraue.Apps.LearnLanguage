using Laraue.Apps.LearnLanguage.AppServices;
using Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Testing;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using Xunit;
using User = Laraue.Apps.LearnLanguage.DataAccess.Entities.User;

namespace Laraue.Apps.LearnEnglish.IntegrationTests;

/// <summary>
/// The My Progress screen and the review quiz built from missed words (MIN-41).
/// </summary>
public class StatsTests : IntegrationTest
{
    [Fact]
    public async Task Stats_ShouldShowProgressOfTheDefaultPair()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var (ruId, deId) = Languages();
        var now = DateTime.UtcNow;
        var user = new User { TelegramId = DefaultUser.Id, GlobalUserId = Guid.NewGuid(), LanguageToLearnId = ruId };
        telegramTestHost.InsertIntoDb(user);

        var a1 = telegramTestHost.GetFromDb(db => db.Words
            .Where(w => w.CefrLevelId == DefaultContextData.CefrLevels.GetId("A1"))
            .OrderBy(w => w.Id)
            .Select(w => new { w.Id, w.Text })
            .Take(4)
            .ToArray());

        // Practised Russian today, yesterday and the day before: a 3-day streak. One old German quiz.
        telegramTestHost.InsertIntoDb(Quiz(user.Id, ruId, now.AddDays(-2), (a1[0].Id, UserQuizQuestionStatus.Correct)));
        telegramTestHost.InsertIntoDb(Quiz(user.Id, ruId, now.AddDays(-1),
            (a1[1].Id, UserQuizQuestionStatus.Incorrect),
            (a1[2].Id, UserQuizQuestionStatus.Incorrect)));
        telegramTestHost.InsertIntoDb(Quiz(user.Id, ruId, now,
            (a1[1].Id, UserQuizQuestionStatus.Incorrect),
            (a1[3].Id, UserQuizQuestionStatus.Skipped)));
        telegramTestHost.InsertIntoDb(Quiz(user.Id, deId, now.AddDays(-30), (a1[0].Id, UserQuizQuestionStatus.Correct)));

        // One word learned today, one in progress.
        telegramTestHost.InsertIntoDb(new LearnedTranslation
        {
            UserId = user.Id, WordId = a1[0].Id, LanguageId = ruId, WinStreakCount = 3, LearnedAt = now,
        });
        telegramTestHost.InsertIntoDb(new LearnedTranslation
        {
            UserId = user.Id, WordId = a1[1].Id, LanguageId = ruId, WinStreakCount = 0,
        });

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Stat)));

        var text = telegramTestHost.Requests().Single<EditMessageTextRequest>().Text;
        var a1LevelId = DefaultContextData.CefrLevels.GetId("A1");
        var counts = telegramTestHost.GetFromDb(db => new
        {
            Total = db.Translations.Count(t => t.LanguageId == ruId),
            A1 = db.Translations.Count(t => t.LanguageId == ruId && t.Word.CefrLevelId == a1LevelId),
        });
        var (total, a1Total) = (counts.Total, counts.A1);

        Assert.Contains("<b>📊 My progress · English → Russian</b>", text);
        Assert.Contains("🔥 3 days in a row", text);
        // The German quiz is outside the pair; the skip isn't an answer.
        Assert.Contains("📅 This week: 3 quizzes · 4 answers · 25% correct", text);
        Assert.Contains("🔷 Learned: 1", text);
        Assert.Contains("🔶 In progress: 1", text);
        Assert.Contains($"📚 Not started yet: {total - 2:N0}", text);
        Assert.Contains($"A1 ░░░░░░░░░░  {100.0 / a1Total:0.0}%", text);
        Assert.Contains($"1/{a1Total}", text);
        Assert.Contains($"{now:ddd}"[..2], text);
        Assert.Contains($"Most missed: {a1[1].Text} ✗2 · {a1[2].Text} ✗1", text);

        var buttons = Buttons(telegramTestHost.Requests().Single<EditMessageTextRequest>());
        Assert.Equal(("🎯 Review missed words", $"1 rvq?lt={ruId}"), buttons[0][0]);
        Assert.Equal(("🎯 New Quiz", $"1 sq?lt={ruId}"), buttons[0][1]);
        Assert.Equal(("📊 English → German", $"stat?lt={deId}"), buttons[1][0]);
    }

    [Fact]
    public async Task Stats_ShouldInviteToTheFirstQuiz_WhenUserHasNoPractice()
    {
        using var telegramTestHost = GetTelegramTestHost();
        telegramTestHost.InsertIntoDb(new User { TelegramId = DefaultUser.Id, GlobalUserId = Guid.NewGuid() });

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Stat)));

        var request = telegramTestHost.Requests().Single<EditMessageTextRequest>();
        Assert.StartsWith("No quizzes yet.", request.Text);
        Assert.Equal(("🎯 New Quiz", "quiz"), Buttons(request)[0][0]);
    }

    [Fact]
    public async Task ReviewQuiz_ShouldAskTheMissedWordsThatAreNotLearned()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var (ruId, _) = Languages();
        var user = new User { TelegramId = DefaultUser.Id, GlobalUserId = Guid.NewGuid(), LanguageToLearnId = ruId };
        telegramTestHost.InsertIntoDb(user);
        var words = telegramTestHost.GetFromDb(db => db.Words.OrderBy(w => w.Id).Select(w => w.Id).Take(3).ToArray());

        // Missed twice and once; the third word was missed but has been learned since.
        telegramTestHost.InsertIntoDb(Quiz(user.Id, ruId, DateTime.UtcNow.AddDays(-1),
            (words[0], UserQuizQuestionStatus.Incorrect),
            (words[0], UserQuizQuestionStatus.Incorrect),
            (words[1], UserQuizQuestionStatus.Incorrect),
            (words[2], UserQuizQuestionStatus.Incorrect)));
        telegramTestHost.InsertIntoDb(new LearnedTranslation
        {
            UserId = user.Id, WordId = words[2], LanguageId = ruId, WinStreakCount = 3, LearnedAt = DateTime.UtcNow,
        });

        await telegramTestHost.SendUpdateAsync(Callback(
            new CallbackRoutePath(TelegramRoutes.ReviewQuiz, RouteMethod.Post)
                .WithTranslationDirection(new SelectedTranslation(ruId))));

        var quiz = telegramTestHost.GetFromDb(db => db.UserQuizzes
            .Include(q => q.UserQuizQuestions)
            .Single(q => q.Status == UserQuizStatus.Active));
        Assert.Equal(ruId, quiz.LanguageId);
        Assert.Equal(new[] { words[0], words[1] }, quiz.UserQuizQuestions.Select(q => q.WordId).Order().ToArray());
        Assert.All(quiz.UserQuizQuestions, q => Assert.Equal(4, q.OptionIds.Length));
        Assert.StartsWith("Question 1/2", telegramTestHost.Requests().Last<EditMessageTextRequest>().Text);
    }

    private static (long Ru, long De) Languages()
    {
        var languages = DefaultContextData.GetWordLanguages();
        return (languages.GetId("ru"), languages.GetId("de"));
    }

    private static UserQuiz Quiz(Guid userId, long languageId, DateTime at, params (long WordId, UserQuizQuestionStatus Status)[] questions) => new()
    {
        UserId = userId,
        LanguageId = languageId,
        Status = UserQuizStatus.Finished,
        CreatedAt = at,
        FinishedAt = at,
        UserQuizQuestions = questions
            .Select(q => new UserQuizQuestion { WordId = q.WordId, Status = q.Status, AnsweredAt = at })
            .ToList(),
    };

    private static (string, string?)[][] Buttons(EditMessageTextRequest request) =>
        ((InlineKeyboardMarkup)request.ReplyMarkup!).InlineKeyboard
            .Select(row => row.Select(b => (b.Text, b.CallbackData)).ToArray())
            .ToArray();

    private static Update Callback(CallbackRoutePath route) => new()
    {
        CallbackQuery = new CallbackQuery
        {
            From = DefaultUser,
            Data = route,
            Message = new Message { Id = 1 },
        },
    };
}
