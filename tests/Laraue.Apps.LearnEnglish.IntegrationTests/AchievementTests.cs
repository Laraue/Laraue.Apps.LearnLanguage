using Laraue.Apps.LearnLanguage.AppServices;
using Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;
using Laraue.Apps.LearnLanguage.AppServices.Services.Achievements;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Laraue.Apps.LearnLanguage.Contracts.Enums;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Testing;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using Xunit;
using User = Laraue.Apps.LearnLanguage.DataAccess.Entities.User;

namespace Laraue.Apps.LearnEnglish.IntegrationTests;

/// <summary>
/// Achievements granted when a quiz finishes and the message about them (MIN-43).
/// </summary>
public class AchievementTests : IntegrationTest
{
    private static readonly long Ru = DefaultContextData.GetWordLanguages().GetId("ru");

    [Fact]
    public async Task PerfectFirstQuiz_ShouldGrantAchievements_AndSendAMessageAboutThem()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id);

        await PlayQuizAsync(telegramTestHost, mistakes: 0);
        await CheckAchievementsAsync(telegramTestHost);

        Assert.Equal(
            [(Achievement.WithoutMistakes, (byte)1), (Achievement.FirstQuiz, (byte)1)],
            Granted(telegramTestHost, me));

        var message = telegramTestHost.Requests().Single<SendMessageRequest>();
        Assert.Equal(
            """
            <b>🎉 New achievements</b>

            <b>🎯 🥉 Sharp eye</b>
            A quiz without mistakes · ✨ Only you have it

            <b>👣 First steps</b>
            First quiz finished · ✨ Only you have it
            """.ReplaceLineEndings("\n"),
            message.Text.TrimEnd());
        var buttons = Buttons(message.ReplyMarkup);
        Assert.Equal([("👌 OK", "d"), ("📤 Share", null)], buttons[0]);
        Assert.Equal([("🔕 Don't notify me", "1 ach/off")], buttons[1]);

        // Share opens Telegram's share dialog with the rarest achievement and what it means, without a link:
        // the bot's @username is clickable (MIN-52).
        var shareUrl = ((InlineKeyboardMarkup)message.ReplyMarkup!).InlineKeyboard.First().Last().Url!;
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(shareUrl).Query);
        Assert.StartsWith("https://t.me/share/url?", shareUrl);
        Assert.Equal("url", Assert.Single(query.AllKeys));
        Assert.Equal(
            "My new achievement with @TestBot: 🎯 🥉 Sharp eye (a quiz without mistakes). ✨ Only you have it",
            query["url"]);
    }

    [Fact]
    public async Task FinishedQuiz_ShouldOnlyRequestACheck_WhichTheJobDoesAndClears()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id);

        await PlayQuizAsync(telegramTestHost, mistakes: 0);

        // Nothing granted or sent while finishing the quiz.
        Assert.Empty(Granted(telegramTestHost, me));
        Assert.Empty(telegramTestHost.Requests().OfType<SendMessageRequest>());
        Assert.NotNull(telegramTestHost.GetFromDb(db => db.Users.Single(u => u.Id == me.Id)).AchievementsCheckRequestedAt);

        Assert.Equal(1, await CheckAchievementsAsync(telegramTestHost));

        Assert.NotEmpty(Granted(telegramTestHost, me));
        Assert.Single(telegramTestHost.Requests().OfType<SendMessageRequest>());
        Assert.Null(telegramTestHost.GetFromDb(db => db.Users.Single(u => u.Id == me.Id)).AchievementsCheckRequestedAt);
        Assert.Equal(0, await CheckAchievementsAsync(telegramTestHost));
    }

    [Fact]
    public async Task QuizFinishedDuringACheck_ShouldKeepTheRequest()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id);
        await PlayQuizAsync(telegramTestHost, mistakes: 0);

        // Another quiz finishes while the job is telling about the first one.
        using var scope = telegramTestHost.CreateScope();
        var services = scope.ServiceProvider;
        var checker = new AchievementChecker(
            services.GetRequiredService<DatabaseContext>(),
            services.GetRequiredService<IAchievementService>(),
            new RequestingAgainNotifier(services.GetRequiredService<IAchievementChecker>(), me.Id),
            services.GetRequiredService<IDateTimeProvider>(),
            NullLogger<AchievementChecker>.Instance);
        await checker.CheckRequestedAsync();

        Assert.NotEmpty(Granted(telegramTestHost, me));
        Assert.NotNull(telegramTestHost.GetFromDb(db => db.Users.Single(u => u.Id == me.Id)).AchievementsCheckRequestedAt);
    }

    [Fact]
    public async Task QuizWithMistakes_ShouldNotGrantWithoutMistakes_AndShowRarity()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id);
        // Two other players, one of them already has the first quiz achievement: 2 of 3 have it now.
        var anna = AddUser(telegramTestHost, 2);
        AddUser(telegramTestHost, 3);
        AddFinishedQuiz(telegramTestHost, anna, DateTime.UtcNow.AddDays(-1));
        AddFinishedQuiz(telegramTestHost, telegramTestHost.GetFromDb(db => db.Users.Single(u => u.TelegramId == 3)),
            DateTime.UtcNow.AddDays(-1));
        telegramTestHost.InsertIntoDb(new UserAchievement
        {
            UserId = anna.Id, Achievement = Achievement.FirstQuiz, Level = 1, AchievedAt = DateTime.UtcNow,
        });

        await PlayQuizAsync(telegramTestHost, mistakes: 1);
        await CheckAchievementsAsync(telegramTestHost);

        Assert.Equal([(Achievement.FirstQuiz, (byte)1)], Granted(telegramTestHost, me));
        Assert.Equal(
            """
            <b>🎉 New achievement</b>

            <b>👣 First steps</b>
            First quiz finished · 67% of players have it
            """.ReplaceLineEndings("\n"),
            telegramTestHost.Requests().Single<SendMessageRequest>().Text.TrimEnd());
    }

    [Fact]
    public async Task ExistingProgress_ShouldGrantEveryLevelReached_AndTellAboutTheHighest()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id);
        var words = telegramTestHost.GetFromDb(db => db.Translations
            .Where(t => t.LanguageId == Ru)
            .OrderBy(t => t.WordId)
            .Select(t => t.WordId)
            // A margin: the quiz's wrong answer may un-learn one of them.
            .Take(1020)
            .ToArray());
        telegramTestHost.InsertRangeIntoDb(words.Select(id => new LearnedTranslation
        {
            UserId = me.Id, WordId = id, LanguageId = Ru, WinStreakCount = 3, LearnedAt = DateTime.UtcNow,
        }));

        await PlayQuizAsync(telegramTestHost, mistakes: 1);
        await CheckAchievementsAsync(telegramTestHost);

        var granted = Granted(telegramTestHost, me);
        Assert.Contains((Achievement.WordsLearned, (byte)1), granted);
        Assert.Contains((Achievement.WordsLearned, (byte)2), granted);
        Assert.Contains((Achievement.WordsLearned, (byte)3), granted);
        Assert.DoesNotContain((Achievement.WordsLearned, (byte)4), granted);

        var text = telegramTestHost.Requests().Single<SendMessageRequest>().Text;
        Assert.Contains("<b>📚 🥇 Scholar</b>", text);
        Assert.Contains("1,000 words learned", text);
        Assert.DoesNotContain("Bookworm", text);
        Assert.DoesNotContain("Word collector", text);
    }

    [Fact]
    public async Task AchievementsAlreadyHeld_ShouldNotBeGrantedOrAnnouncedAgain()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id);

        await PlayQuizAsync(telegramTestHost, mistakes: 0);
        await CheckAchievementsAsync(telegramTestHost);
        await PlayQuizAsync(telegramTestHost, mistakes: 1);
        await CheckAchievementsAsync(telegramTestHost);

        Assert.Equal(2, Granted(telegramTestHost, me).Length);
        Assert.Single(telegramTestHost.Requests().OfType<SendMessageRequest>());
    }

    [Fact]
    public async Task QuizWithoutAnswers_ShouldGrantNothing()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id);

        await telegramTestHost.SendUpdateAsync(Callback(StartQuizRoute()));
        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.FinishQuiz, RouteMethod.Post)));
        await CheckAchievementsAsync(telegramTestHost);

        Assert.Empty(Granted(telegramTestHost, me));
        Assert.Empty(telegramTestHost.Requests().OfType<SendMessageRequest>());
    }

    [Theory]
    [InlineData(8, true)]
    [InlineData(6, false)]
    public async Task QuizAfterAWeekAway_ShouldWelcomeBack(int daysAway, bool welcomed)
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id);
        AddFinishedQuiz(telegramTestHost, me, DateTime.UtcNow.AddDays(-daysAway));

        await PlayQuizAsync(telegramTestHost, mistakes: 1);
        await CheckAchievementsAsync(telegramTestHost);

        Assert.Equal(welcomed, Granted(telegramTestHost, me).Contains((Achievement.WelcomeBack, (byte)1)));
    }

    [Fact]
    public async Task AchievementMessage_ShouldBeInTheUsersLanguage()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id);
        await telegramTestHost.ExecuteInDbAsync(db => db.Users
            .Where(u => u.Id == me.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.TelegramLanguageCode, "ru")));

        await PlayQuizAsync(telegramTestHost, mistakes: 1);
        await CheckAchievementsAsync(telegramTestHost);

        Assert.Contains(
            "<b>👣 Первые шаги</b>\nПервый тест пройден",
            telegramTestHost.Requests().Single<SendMessageRequest>().Text);
    }

    [Fact]
    public async Task LanguagePairs_ShouldCountPairsWithALearnedWord()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id);
        var de = DefaultContextData.GetWordLanguages().GetId("de");
        var word = telegramTestHost.GetFromDb(db => db.Translations
            .Where(t => t.LanguageId == de)
            .OrderBy(t => t.WordId)
            .First()).WordId;
        // No mistakes: a wrong answer would un-learn a word asked again for review.
        // A quiz in German only: not a pair yet.
        AddFinishedQuiz(telegramTestHost, me, DateTime.UtcNow.AddHours(-1), de);
        AddLearned(telegramTestHost, me, word, Ru);

        await PlayQuizAsync(telegramTestHost, mistakes: 0);
        await CheckAchievementsAsync(telegramTestHost);
        Assert.DoesNotContain((Achievement.LanguagePairs, (byte)1), Granted(telegramTestHost, me));

        // A word learned in German: two pairs.
        AddLearned(telegramTestHost, me, word, de);
        await PlayQuizAsync(telegramTestHost, mistakes: 0);
        await CheckAchievementsAsync(telegramTestHost);
        Assert.Contains((Achievement.LanguagePairs, (byte)1), Granted(telegramTestHost, me));
    }

    [Fact]
    public async Task QuizzesInADay_ShouldGrantALevelPerThreshold_CountingOnlyToday()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id);
        // Yesterday's quizzes don't count.
        for (var i = 0; i < 30; i++)
            AddFinishedQuiz(telegramTestHost, me, DateTime.UtcNow.Date.AddMinutes(-1));
        for (var i = 0; i < 24; i++)
            AddFinishedQuiz(telegramTestHost, me, DateTime.UtcNow);

        // The 25th quiz today (MIN-53).
        await PlayQuizAsync(telegramTestHost, mistakes: 1);
        await CheckAchievementsAsync(telegramTestHost);

        var granted = Granted(telegramTestHost, me);
        Assert.Contains((Achievement.TenQuizzesInADay, (byte)1), granted);
        Assert.Contains((Achievement.TenQuizzesInADay, (byte)2), granted);
        Assert.DoesNotContain((Achievement.TenQuizzesInADay, (byte)3), granted);
        var text = telegramTestHost.Requests().Single<SendMessageRequest>().Text;
        Assert.Contains("<b>⚡ 🥈 Ultramarathon</b>\n25 quizzes in one day", text);
    }

    [Fact]
    public async Task C1Words_ShouldGrantALevelPerThreshold_CountingC1WordsOnly()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id);
        var c1Words = telegramTestHost.GetFromDb(db => db.Translations
            .Where(t => t.LanguageId == Ru && t.Word.CefrLevel!.Name == "C1")
            .Select(t => t.WordId)
            .Distinct()
            .OrderBy(id => id)
            .Take(100)
            .ToArray());
        Assert.Equal(100, c1Words.Length);
        telegramTestHost.InsertRangeIntoDb(c1Words.Select(id => new LearnedTranslation
        {
            UserId = me.Id, WordId = id, LanguageId = Ru, WinStreakCount = 3, LearnedAt = DateTime.UtcNow,
        }));

        // No mistakes: a wrong answer would un-learn a word asked again for review (MIN-53).
        await PlayQuizAsync(telegramTestHost, mistakes: 0);
        await CheckAchievementsAsync(telegramTestHost);

        var granted = Granted(telegramTestHost, me);
        Assert.Contains((Achievement.FirstC1Word, (byte)1), granted);
        Assert.Contains((Achievement.FirstC1Word, (byte)2), granted);
        Assert.DoesNotContain((Achievement.FirstC1Word, (byte)3), granted);
        var text = telegramTestHost.Requests().Single<SendMessageRequest>().Text;
        Assert.Contains("<b>🔤 🥈 Pathfinder</b>\n100 C1 words learned", text);
    }

    [Fact]
    public async Task MutedUser_ShouldEarnAchievements_WithoutAMessage()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id, messagesOff: true);

        await PlayQuizAsync(telegramTestHost, mistakes: 0);
        await CheckAchievementsAsync(telegramTestHost);

        Assert.NotEmpty(Granted(telegramTestHost, me));
        Assert.Empty(telegramTestHost.Requests().OfType<SendMessageRequest>());
    }

    [Fact]
    public async Task DontNotifyButton_ShouldTurnMessagesOff_AndRemoveTheMessage()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id);

        await telegramTestHost.SendUpdateAsync(Callback(
            new CallbackRoutePath(TelegramRoutes.AchievementMessagesOff, RouteMethod.Post), messageId: 7));

        Assert.True(telegramTestHost.GetFromDb(db => db.Users.Single(u => u.Id == me.Id)).AchievementMessagesOff);
        Assert.Equal(7, telegramTestHost.Requests().Single<DeleteMessageRequest>().MessageId);
    }

    [Fact]
    public async Task Settings_ShouldTurnAchievementMessagesOnAndOff()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id);

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Settings)));
        var settings = telegramTestHost.Requests().Single<EditMessageTextRequest>();
        Assert.Contains("🏅 Achievement messages: <b>on</b>", settings.Text);
        var off = Buttons(settings.ReplyMarkup).SelectMany(r => r)
            .Single(b => b.Item1 == "🔕 Turn off achievement messages");

        await telegramTestHost.SendUpdateAsync(Callback(off.Item2!));

        Assert.True(telegramTestHost.GetFromDb(db => db.Users.Single(u => u.Id == me.Id)).AchievementMessagesOff);
        var updated = telegramTestHost.Requests().Last<EditMessageTextRequest>();
        Assert.Contains("🏅 Achievement messages: <b>off</b>", updated.Text);
        Assert.Contains(
            Buttons(updated.ReplyMarkup).SelectMany(r => r),
            b => b.Item1 == "🔔 Turn on achievement messages");
    }

    /// <summary>
    /// Starts a quiz and answers its questions: the first <paramref name="mistakes"/> wrong, the rest right.
    /// </summary>
    private static async Task PlayQuizAsync(AppTelegramTestHost host, int mistakes)
    {
        await host.SendUpdateAsync(Callback(StartQuizRoute()));
        var questions = host.GetFromDb(db => db.UserQuizQuestions
            .Where(q => q.Quiz.Status == UserQuizStatus.Active)
            .OrderBy(q => q.Id)
            .ToArray());

        foreach (var (question, index) in questions.Select((q, i) => (q, i)))
        {
            var answer = index < mistakes ? question.OptionIds.First(id => id != question.WordId) : question.WordId;
            await host.SendUpdateAsync(Callback(
                new CallbackRoutePath(TelegramRoutes.SelectQuizAnswer, RouteMethod.Post)
                    .WithQueryParameter(ParameterNames.QuestionId, question.Id)
                    .WithQueryParameter(ParameterNames.AnswerWordId, answer)));
        }
    }

    private static async Task<int> CheckAchievementsAsync(AppTelegramTestHost host)
    {
        using var scope = host.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAchievementChecker>().CheckRequestedAsync();
    }

    private sealed class RequestingAgainNotifier(IAchievementChecker checker, Guid userId) : IAchievementNotifier
    {
        public async Task NotifyAsync(
            Guid notifiedUserId,
            ChatId chatId,
            IReadOnlyList<EarnedAchievement> earned,
            CancellationToken ct = default)
        {
            // A later time than the request being checked.
            await Task.Delay(10, ct);
            await checker.RequestCheckAsync(userId, ct);
        }
    }

    private static CallbackRoutePath StartQuizRoute() =>
        new CallbackRoutePath(TelegramRoutes.StartQuiz, RouteMethod.Post)
            .WithTranslationDirection(new SelectedTranslation(Ru));

    private static User AddUser(AppTelegramTestHost host, long telegramId, bool messagesOff = false)
    {
        var user = new User
        {
            TelegramId = telegramId,
            GlobalUserId = Guid.NewGuid(),
            LanguageToLearnId = Ru,
            AchievementMessagesOff = messagesOff,
        };
        host.InsertIntoDb(user);
        return user;
    }

    private static void AddLearned(AppTelegramTestHost host, User user, long wordId, long languageId) =>
        host.InsertIntoDb(new LearnedTranslation
        {
            UserId = user.Id, WordId = wordId, LanguageId = languageId, WinStreakCount = 3, LearnedAt = DateTime.UtcNow,
        });

    private static void AddFinishedQuiz(AppTelegramTestHost host, User user, DateTime at, long? languageId = null)
    {
        var wordId = host.GetFromDb(db => db.Words.OrderBy(w => w.Id).First()).Id;
        host.InsertIntoDb(new UserQuiz
        {
            UserId = user.Id,
            LanguageId = languageId ?? Ru,
            Status = UserQuizStatus.Finished,
            CreatedAt = at,
            FinishedAt = at,
            UserQuizQuestions =
            [
                new UserQuizQuestion { WordId = wordId, Status = UserQuizQuestionStatus.Correct, AnsweredAt = at },
            ],
        });
    }

    private static (Achievement, byte)[] Granted(AppTelegramTestHost host, User user) =>
        host.GetFromDb(db => db.UserAchievements
            .Where(a => a.UserId == user.Id)
            .OrderBy(a => a.Achievement)
            .ThenBy(a => a.Level)
            .Select(a => new { a.Achievement, a.Level })
            .ToArray())
        .Select(a => (a.Achievement, a.Level))
        .ToArray();

    private static (string, string?)[][] Buttons(ReplyMarkup? markup) =>
        ((InlineKeyboardMarkup)markup!).InlineKeyboard
            .Select(row => row.Select(b => (b.Text, b.CallbackData)).ToArray())
            .ToArray();

    private static Update Callback(string data, int messageId = 1) => new()
    {
        CallbackQuery = new CallbackQuery
        {
            From = DefaultUser,
            Data = data,
            Message = new Message { Id = messageId },
        },
    };
}
