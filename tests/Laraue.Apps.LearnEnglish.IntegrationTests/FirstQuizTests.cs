using Laraue.Apps.LearnLanguage.AppServices;
using Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;
using Laraue.Apps.LearnLanguage.AppServices.Services.Quiz;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Testing;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Xunit;
using User = Laraue.Apps.LearnLanguage.DataAccess.Entities.User;

namespace Laraue.Apps.LearnEnglish.IntegrationTests;

/// <summary>
/// The path of a new user from /start to the first question (MIN-29).
/// </summary>
public class FirstQuizTests : IntegrationTest
{
    [Fact]
    public async Task Start_ShouldOfferFirstQuizInTelegramLanguage_ForNewUser()
    {
        using var telegramTestHost = GetTelegramTestHost();
        await telegramTestHost.SendUpdateAsync(StartMessage(languageCode: "de-AT"));

        var user = Assert.Single(telegramTestHost.GetFromDb(db => db.Users.ToArray()));
        var germanId = DefaultContextData.GetWordLanguages().GetId("de");
        Assert.Equal(germanId, user.LanguageToLearnId);

        var request = telegramTestHost.Requests().Single<SendMessageRequest>();
        Assert.Equal<(string, string?)[][]>(
            [
                [("▶️ Start: 6 easy words", $"1 sfq?lt={germanId}")],
                [("Menu", "m")],
            ],
            Buttons(request));
    }

    [Theory]
    [InlineData("uz", "uz")]
    [InlineData("ar", "ar")]
    [InlineData("fa-IR", "fa")]
    public async Task Start_ShouldOfferFirstQuiz_ForUzbekArabicAndPersianSpeakers(string languageCode, string pair)
    {
        using var telegramTestHost = GetTelegramTestHost();
        await telegramTestHost.SendUpdateAsync(StartMessage(languageCode));

        var pairId = DefaultContextData.GetWordLanguages().GetId(pair);
        var user = Assert.Single(telegramTestHost.GetFromDb(db => db.Users.ToArray()));
        Assert.Equal(pairId, user.LanguageToLearnId);

        var request = telegramTestHost.Requests().Single<SendMessageRequest>();
        Assert.Equal($"1 sfq?lt={pairId}", Buttons(request)[0][0].Item2);

        // The first quiz has enough A1 words in the new pair (MIN-36).
        await telegramTestHost.SendUpdateAsync(StartFirstQuizCallback(pairId));
        var questions = telegramTestHost.GetFromDb(db => db.UserQuizQuestions.ToArray());
        Assert.Equal(QuizService.FirstQuizQuestionsCount, questions.Length);
    }

    [Fact]
    public async Task Start_ShouldAskLanguageForFirstQuiz_WhenTelegramLanguageIsEnglish()
    {
        using var telegramTestHost = GetTelegramTestHost();
        await telegramTestHost.SendUpdateAsync(StartMessage(languageCode: "en"));

        var user = Assert.Single(telegramTestHost.GetFromDb(db => db.Users.ToArray()));
        Assert.Null(user.LanguageToLearnId);

        var request = telegramTestHost.Requests().Single<SendMessageRequest>();
        Assert.Equal<(string, string?)[][]>(
            [
                [("English → Arabic", "1 sfq?lt=10")],
                [("English → German", "1 sfq?lt=6")],
                [("English → Spanish", "1 sfq?lt=5")],
                [("English → Persian", "1 sfq?lt=11")],
                [("English → French", "1 sfq?lt=3")],
                [("English → Hindi", "1 sfq?lt=8")],
                [("English → Japanese", "1 sfq?lt=4")],
                [("English → Russian", "1 sfq?lt=2")],
                [("English → Uzbek", "1 sfq?lt=9")],
                [("English → Chinese", "1 sfq?lt=7")],
                [("Menu", "m")],
            ],
            Buttons(request));
    }

    [Fact]
    public async Task Start_ShouldShowMenu_WhenUserHadQuizzes()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var user = new User { TelegramId = DefaultUser.Id, GlobalUserId = Guid.NewGuid() };
        telegramTestHost.InsertIntoDb(user);
        telegramTestHost.InsertIntoDb(new UserQuiz
        {
            UserId = user.Id,
            LanguageId = DefaultContextData.GetWordLanguages().GetId("ru"),
            Status = UserQuizStatus.Finished,
            CreatedAt = DateTime.UtcNow,
        });

        await telegramTestHost.SendUpdateAsync(StartMessage(languageCode: null));

        var request = telegramTestHost.Requests().Single<SendMessageRequest>();
        Assert.Equal<(string, string?)[][]>(
            [
                [("🎯 New Quiz", "quiz")],
                [("📊 My Progress", "stat")],
                [("⚙️ Settings", "s")],
                [("📋 What's New", null)],
            ],
            Buttons(request));
    }

    [Fact]
    public async Task StartFirstQuiz_ShouldSaveLanguageAndShowShortEasyQuiz()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var ruLanguageId = DefaultContextData.GetWordLanguages().GetId("ru");

        await telegramTestHost.SendUpdateAsync(StartFirstQuizCallback(ruLanguageId));

        var user = Assert.Single(telegramTestHost.GetFromDb(db => db.Users.ToArray()));
        Assert.Equal(ruLanguageId, user.LanguageToLearnId);

        var request = telegramTestHost.Requests().Single<EditMessageTextRequest>();
        Assert.StartsWith("Question 1/6", request.Text);

        var questions = telegramTestHost.GetFromDb(db => db.UserQuizQuestions
            .Include(q => q.Word)
            .ToArray());
        Assert.Equal(QuizService.FirstQuizQuestionsCount, questions.Length);
        Assert.All(questions, q => Assert.Equal(4, q.OptionIds.Length));
        var a1Id = DefaultContextData.CefrLevels.GetId("A1");
        Assert.All(questions, q => Assert.Equal(a1Id, q.Word.CefrLevelId));
    }

    [Fact]
    public async Task StartFirstQuiz_ShouldUseWordsOtherUsersHaveMet()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var ruLanguageId = DefaultContextData.GetWordLanguages().GetId("ru");
        var a1Id = DefaultContextData.CefrLevels.GetId("A1");

        // Another user has met the first A1 word. It is still new for this user.
        var firstA1WordId = telegramTestHost.GetFromDb(db => db.Words
            .Where(w => w.CefrLevelId == a1Id)
            .OrderBy(w => w.Id)
            .Select(w => new { w.Id })
            .First()).Id;
        var otherUser = new User { TelegramId = 999, GlobalUserId = Guid.NewGuid() };
        telegramTestHost.InsertIntoDb(otherUser);
        telegramTestHost.InsertIntoDb(new LearnedTranslation
        {
            UserId = otherUser.Id,
            WordId = firstA1WordId,
            LanguageId = ruLanguageId,
            WinStreakCount = 1,
        });

        await telegramTestHost.SendUpdateAsync(StartFirstQuizCallback(ruLanguageId));

        var questionWordIds = telegramTestHost.GetFromDb(db => db.UserQuizQuestions
            .Where(q => q.Quiz.User.TelegramId == DefaultUser.Id)
            .Select(q => q.WordId)
            .ToArray());
        Assert.Contains(firstA1WordId, questionWordIds);
    }

    [Fact]
    public async Task FirstQuizResults_ShouldShowWordsInProgress()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var ruLanguageId = DefaultContextData.GetWordLanguages().GetId("ru");

        await telegramTestHost.SendUpdateAsync(StartFirstQuizCallback(ruLanguageId));

        var questions = telegramTestHost.GetFromDb(db => db.UserQuizQuestions
            .OrderBy(q => q.Id)
            .ToArray());
        foreach (var question in questions)
        {
            await telegramTestHost.SendUpdateAsync(Callback(
                new CallbackRoutePath(TelegramRoutes.SelectQuizAnswer, RouteMethod.Post)
                    .WithQueryParameter(ParameterNames.QuestionId, question.Id)
                    .WithQueryParameter(ParameterNames.AnswerWordId, question.WordId)));
        }

        var results = telegramTestHost.Requests().Last<EditMessageTextRequest>();
        Assert.Contains("Quiz results", results.Text);
        Assert.Contains(
            "🔶 +6 words in progress (3 correct in a row to learn)",
            results.Text);

        // The first finished quiz offers the daily reminder (MIN-30).
        var firstButton = ((InlineKeyboardMarkup)results.ReplyMarkup!).InlineKeyboard.First().First();
        Assert.Equal(("🔔 Remind me daily at this time", "1 s/rem?e=true"), (firstButton.Text, firstButton.CallbackData));
    }

    [Fact]
    public async Task RemindersSetting_ShouldTurnDailyReminderOnAndOff()
    {
        using var telegramTestHost = GetTelegramTestHost();
        telegramTestHost.InsertIntoDb(new User
        {
            TelegramId = DefaultUser.Id,
            GlobalUserId = Guid.NewGuid(),
            BotBlockedAt = DateTime.UtcNow.AddDays(-1),
        });

        var before = TimeOnly.FromDateTime(DateTime.UtcNow);
        await telegramTestHost.SendUpdateAsync(Callback(
            new CallbackRoutePath(TelegramRoutes.RemindersSettings, RouteMethod.Post)
                .WithQueryParameter(ParameterNames.Enable, true)));

        var user = Assert.Single(telegramTestHost.GetFromDb(db => db.Users.ToArray()));
        Assert.NotNull(user.ReminderTime);
        Assert.InRange(user.ReminderTime.Value, before, TimeOnly.FromDateTime(DateTime.UtcNow));
        Assert.Null(user.BotBlockedAt);

        var settings = telegramTestHost.Requests().Last<EditMessageTextRequest>();
        Assert.Contains($"🔔 Daily reminder: <b>on, around {user.ReminderTime.Value:HH:mm} UTC</b>", settings.Text);

        await telegramTestHost.SendUpdateAsync(Callback(
            new CallbackRoutePath(TelegramRoutes.RemindersSettings, RouteMethod.Post)
                .WithQueryParameter(ParameterNames.Enable, false)));

        Assert.Null(Assert.Single(telegramTestHost.GetFromDb(db => db.Users.ToArray())).ReminderTime);
        Assert.Contains(
            "🔔 Daily reminder: <b>off</b>",
            telegramTestHost.Requests().Last<EditMessageTextRequest>().Text);
    }

    private static (string, string?)[][] Buttons(SendMessageRequest request) =>
        ((InlineKeyboardMarkup)request.ReplyMarkup!).InlineKeyboard
            .Select(row => row.Select(b => (b.Text, b.CallbackData)).ToArray())
            .ToArray();

    private static Update StartMessage(string? languageCode) => new()
    {
        Message = new Message
        {
            Text = "/start",
            From = new global::Telegram.Bot.Types.User
            {
                Id = DefaultUser.Id,
                Username = DefaultUser.Username,
                LanguageCode = languageCode,
            },
            Chat = new Chat
            {
                Id = DefaultUser.Id,
                Type = ChatType.Private,
            },
        },
    };

    private static Update StartFirstQuizCallback(long languageToLearnId) => Callback(
        new CallbackRoutePath(TelegramRoutes.StartFirstQuiz, RouteMethod.Post)
            .WithTranslationDirection(new SelectedTranslation(languageToLearnId)));

    private static Update Callback(CallbackRoutePath route) => new()
    {
        CallbackQuery = new CallbackQuery
        {
            From = DefaultUser,
            Data = route,
            Message = new Message
            {
                Id = 1,
            },
        },
    };
}
