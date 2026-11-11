using Laraue.Apps.LearnLanguage.AppServices;
using Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;
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

        Assert.Equal(
            [(Achievement.WithoutMistakes, (byte)1), (Achievement.FirstQuiz, (byte)1)],
            Granted(telegramTestHost, me));

        var message = telegramTestHost.Requests().Single<SendMessageRequest>();
        Assert.Equal(
            """
            <b>🎉 New achievement: 🎯 Without mistakes · 🥉 Sharp eye</b>
            🥇 Nobody else has it yet

            <b>🎉 New achievement: 👣 First steps</b>
            🥇 Nobody else has it yet
            """.ReplaceLineEndings("\n"),
            message.Text.TrimEnd());
        Assert.Equal(
            [("👌 OK", "d"), ("🔕 Don't notify me", "1 ach/off")],
            Buttons(message.ReplyMarkup).Single());
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

        Assert.Equal([(Achievement.FirstQuiz, (byte)1)], Granted(telegramTestHost, me));
        Assert.Equal(
            """
            <b>🎉 New achievement: 👣 First steps</b>
            67% of players have it
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

        var granted = Granted(telegramTestHost, me);
        Assert.Contains((Achievement.WordsLearned, (byte)1), granted);
        Assert.Contains((Achievement.WordsLearned, (byte)2), granted);
        Assert.Contains((Achievement.WordsLearned, (byte)3), granted);
        Assert.DoesNotContain((Achievement.WordsLearned, (byte)4), granted);

        var text = telegramTestHost.Requests().Single<SendMessageRequest>().Text;
        Assert.Contains("📚 Words learned · 🥇 Scholar", text);
        Assert.DoesNotContain("Bookworm", text);
        Assert.DoesNotContain("Word collector", text);
    }

    [Fact]
    public async Task AchievementsAlreadyHeld_ShouldNotBeGrantedOrAnnouncedAgain()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id);

        await PlayQuizAsync(telegramTestHost, mistakes: 0);
        await PlayQuizAsync(telegramTestHost, mistakes: 1);

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

        Assert.Equal(welcomed, Granted(telegramTestHost, me).Contains((Achievement.WelcomeBack, (byte)1)));
    }

    [Fact]
    public async Task MutedUser_ShouldEarnAchievements_WithoutAMessage()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id, messagesOff: true);

        await PlayQuizAsync(telegramTestHost, mistakes: 0);

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

    private static void AddFinishedQuiz(AppTelegramTestHost host, User user, DateTime at)
    {
        var wordId = host.GetFromDb(db => db.Words.OrderBy(w => w.Id).First()).Id;
        host.InsertIntoDb(new UserQuiz
        {
            UserId = user.Id,
            LanguageId = Ru,
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
