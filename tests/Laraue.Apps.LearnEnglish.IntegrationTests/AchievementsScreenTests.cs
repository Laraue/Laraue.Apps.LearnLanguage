using Laraue.Apps.LearnLanguage.AppServices;
using Laraue.Apps.LearnLanguage.Contracts.Enums;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Testing;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using Xunit;
using TelegramUser = Telegram.Bot.Types.User;
using User = Laraue.Apps.LearnLanguage.DataAccess.Entities.User;

namespace Laraue.Apps.LearnEnglish.IntegrationTests;

/// <summary>
/// The achievements list, titles on the leaderboard, and the line on My Progress (MIN-43).
/// </summary>
public class AchievementsScreenTests : IntegrationTest
{
    private static readonly long Ru = DefaultContextData.GetWordLanguages().GetId("ru");

    [Fact]
    public async Task List_ShouldShowLevelsProgressAndRarity()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id, "Me");
        AddFinishedQuiz(telegramTestHost, me);
        AddLearnedWords(telegramTestHost, me, 120);
        Grant(telegramTestHost, me, Achievement.FirstQuiz, 1);
        Grant(telegramTestHost, me, Achievement.WordsLearned, 1);
        Grant(telegramTestHost, me, Achievement.LanguagePairs, 1);
        Grant(telegramTestHost, me, Achievement.LanguagePairs, 2);

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Achievements)));

        var request = telegramTestHost.Requests().Single<EditMessageTextRequest>();
        var lines = request.Text!.Split('\n');
        Assert.Equal("<b>🏅 Achievements · 4 of 27</b>", lines[0]);
        // A level held, the next one with progress to it; nobody else has the next one yet.
        Assert.Contains("📚 <b>Words learned</b> · 🥉 Bookworm", lines);
        Assert.Contains("Next: 🥈 Word collector · 120 / 500 · Nobody has it yet", lines);
        // Every level held.
        Assert.Contains("🌍 <b>Language pairs</b> · 🥈 Polyglot · 🥇 Nobody else has it yet", lines);
        // Nothing held yet: only the next level; levels completed show the CEFR level instead of a count.
        Assert.Contains("🎓 <b>Levels completed</b>", lines);
        Assert.Contains("Next: 🥉 Peppa Pig without subtitles · A1 · Nobody has it yet", lines);
        // Single achievements as a checklist.
        Assert.Contains("✅ 👣 <b>First steps</b> · 🥇 Nobody else has it yet", lines);
        Assert.Contains("🔒 ⚡ <b>Marathon</b> · 10 quizzes in a day · Nobody has it yet", lines);

        Assert.Equal(("🎖 Choose a title", "achs/t"), Buttons(request)[0][0]);
        Assert.Equal(("📊 My Progress", "stat"), Buttons(request)[1][0]);
    }

    [Fact]
    public async Task Title_ShouldBeChosenFromEarnedLevels_AndShownOnTheLeaderboard()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id, "Me", LeaderboardNameMode.FirstName);
        AddFinishedQuiz(telegramTestHost, me);
        Grant(telegramTestHost, me, Achievement.DaysInARow, 1);
        Grant(telegramTestHost, me, Achievement.DaysInARow, 2);

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.AchievementTitles)));
        var titles = telegramTestHost.Requests().Single<EditMessageTextRequest>();
        Assert.Equal(
            ["🔥 Spark", "🔥 Flame", "✅ Without a title", "← Back"],
            Buttons(titles).Select(r => r.Single().Item1).ToArray());

        var flame = Buttons(titles).SelectMany(r => r).Single(b => b.Item1 == "🔥 Flame").Item2!;
        await telegramTestHost.SendUpdateAsync(Callback(flame));

        var user = telegramTestHost.GetFromDb(db => db.Users.Single(u => u.Id == me.Id));
        Assert.Equal((Achievement.DaysInARow, (byte)2), (user.TitleAchievement!.Value, user.TitleLevel!.Value));
        Assert.Equal(
            "🎖 Title: 🔥 Flame",
            Buttons(telegramTestHost.Requests().Last<EditMessageTextRequest>())[0][0].Item1);

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Leaderboard)));
        Assert.Contains(
            "Me <b>(you)</b> · 🔥 Flame · 3 pts",
            telegramTestHost.Requests().Last<EditMessageTextRequest>().Text);
    }

    [Fact]
    public async Task Title_ShouldNotBeSetToALevelNotEarned()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id, "Me");
        Grant(telegramTestHost, me, Achievement.DaysInARow, 1);

        await telegramTestHost.SendUpdateAsync(Callback(
            new CallbackRoutePath(TelegramRoutes.AchievementTitles, RouteMethod.Post)
                .WithQueryParameter(ParameterNames.Achievement, (int)Achievement.DaysInARow)
                .WithQueryParameter(ParameterNames.Level, 3)));

        Assert.Null(telegramTestHost.GetFromDb(db => db.Users.Single(u => u.Id == me.Id)).TitleAchievement);
    }

    [Fact]
    public async Task MyProgress_ShouldCountAchievements_AndLinkTheList()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id, "Me");
        AddFinishedQuiz(telegramTestHost, me);
        Grant(telegramTestHost, me, Achievement.FirstQuiz, 1);

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Stat)));

        var request = telegramTestHost.Requests().Single<EditMessageTextRequest>();
        Assert.Contains("🏅 Achievements: 1 of 27", request.Text!.Split('\n'));
        Assert.Equal(("🏅 Achievements", "achs"), Buttons(request)[1][0]);
    }

    private static User AddUser(
        AppTelegramTestHost host,
        long telegramId,
        string firstName,
        LeaderboardNameMode nameMode = LeaderboardNameMode.Hidden)
    {
        var user = new User
        {
            TelegramId = telegramId,
            GlobalUserId = Guid.NewGuid(),
            LanguageToLearnId = Ru,
            TelegramFirstName = firstName,
            LeaderboardName = nameMode,
        };
        host.InsertIntoDb(user);
        return user;
    }

    private static void AddFinishedQuiz(AppTelegramTestHost host, User user)
    {
        var wordId = host.GetFromDb(db => db.Words.OrderBy(w => w.Id).First()).Id;
        var now = DateTime.UtcNow;
        host.InsertIntoDb(new UserQuiz
        {
            UserId = user.Id,
            LanguageId = Ru,
            Status = UserQuizStatus.Finished,
            CreatedAt = now,
            FinishedAt = now,
            UserQuizQuestions =
            [
                new UserQuizQuestion { WordId = wordId, Status = UserQuizQuestionStatus.Correct, AnsweredAt = now },
            ],
        });
    }

    private static void AddLearnedWords(AppTelegramTestHost host, User user, int count)
    {
        var words = host.GetFromDb(db => db.Translations
            .Where(t => t.LanguageId == Ru)
            .OrderBy(t => t.WordId)
            .Select(t => t.WordId)
            .Take(count)
            .ToArray());
        host.InsertRangeIntoDb(words.Select(id => new LearnedTranslation
        {
            UserId = user.Id, WordId = id, LanguageId = Ru, WinStreakCount = 3, LearnedAt = DateTime.UtcNow,
        }));
    }

    private static void Grant(AppTelegramTestHost host, User user, Achievement achievement, byte level) =>
        host.InsertIntoDb(new UserAchievement
        {
            UserId = user.Id, Achievement = achievement, Level = level, AchievedAt = DateTime.UtcNow.AddSeconds(level),
        });

    private static (string, string?)[][] Buttons(EditMessageTextRequest request) =>
        ((InlineKeyboardMarkup)request.ReplyMarkup!).InlineKeyboard
            .Select(row => row.Select(b => (b.Text, b.CallbackData)).ToArray())
            .ToArray();

    private static Update Callback(string data) => new()
    {
        CallbackQuery = new CallbackQuery
        {
            From = new TelegramUser { Id = DefaultUser.Id, FirstName = "Me" },
            Data = data,
            Message = new Message { Id = 1 },
        },
    };
}
