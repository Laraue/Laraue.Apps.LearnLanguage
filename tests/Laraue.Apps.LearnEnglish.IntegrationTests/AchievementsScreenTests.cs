using Microsoft.Extensions.DependencyInjection;
using Laraue.Apps.LearnLanguage.AppServices.Services.Achievements;
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
    public async Task List_ShouldShowMedalsAndProgress()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id, "Me");
        AddFinishedQuiz(telegramTestHost, me);
        AddLearnedWords(telegramTestHost, me, 120);
        Grant(telegramTestHost, me, Achievement.FirstQuiz, 1);
        Grant(telegramTestHost, me, Achievement.WordsLearned, 1);
        Grant(telegramTestHost, me, Achievement.LanguagePairs, 1);
        Grant(telegramTestHost, me, Achievement.LanguagePairs, 2);
        // The finished quiz has no mistakes, and the first words include C1 ones.
        Grant(telegramTestHost, me, Achievement.WithoutMistakes, 1);
        Grant(telegramTestHost, me, Achievement.FirstC1Word, 1);

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Achievements)));

        var request = telegramTestHost.Requests().Single<EditMessageTextRequest>();
        var lines = request.Text!.Split('\n');
        Assert.Equal("<b>🏅 Achievements · 6 of 26</b>", lines[0]);
        // The level held, then the next one with the progress to it.
        AssertFollow(lines, "📚 Words learned", "Earned: 🥉 Bookworm", "Next: 🥈 Word collector · 120 of 500 words");
        AssertFollow(lines, "🎯 Without mistakes",
            "Earned: 🥉 Sharp eye", "Next: 🥈 Sniper · 1 of 5 quizzes in a row without mistakes");
        // Nothing held yet: only the next one.
        AssertFollow(lines, "🧩 Quizzes finished", "Next: 🥉 Persistence · 1 of 100 quizzes");
        AssertFollow(lines, "🎓 Levels completed", "Next: 🥉 A1 Beginner · learn every A1 word");
        // Every level held: no next one.
        AssertFollow(lines, "🌍 Language pairs", "Earned: 🥈 Polyglot", "");
        // Single achievements as a checklist.
        Assert.Contains("✅ 👣 First steps · First quiz finished", lines);
        Assert.Contains("✅ 🔤 Pioneer · First C1 word learned", lines);
        // Each on its own, separated by blank lines.
        AssertFollow(lines, "✅ 👣 First steps · First quiz finished", "", "🔒 ⚡ Marathon · 10 quizzes in one day");
        Assert.Contains("🔒 ⚡ Marathon · 10 quizzes in one day", lines);
        // Everything reached is granted: no waiting.
        Assert.DoesNotContain(lines, l => l.StartsWith("⏳"));

        Assert.Equal(("🎖 Choose a title", "achs/t"), Buttons(request)[0][0]);
        Assert.Equal(("📊 My Progress", "stat"), Buttons(request)[1][0]);
    }

    [Fact]
    public async Task List_ShouldRequestACheck_WhenSomethingReachedIsNotGrantedYet()
    {
        using var telegramTestHost = GetTelegramTestHost();
        // Progress from before achievements existed, nothing granted.
        var me = AddUser(telegramTestHost, DefaultUser.Id, "Me");
        AddFinishedQuiz(telegramTestHost, me);
        AddLearnedWords(telegramTestHost, me, 120);

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Achievements)));

        Assert.Contains(
            "⏳ New achievements are on their way: a message about them comes in a few seconds.",
            telegramTestHost.Requests().Single<EditMessageTextRequest>().Text!.Split('\n'));
        Assert.NotNull(telegramTestHost.GetFromDb(db => db.Users.Single(u => u.Id == me.Id)).AchievementsCheckRequestedAt);

        // The achievements job grants them; the screen no longer waits.
        using (var scope = telegramTestHost.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IAchievementChecker>().CheckRequestedAsync();
        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Achievements)));

        var text = telegramTestHost.Requests().Last<EditMessageTextRequest>().Text!;
        Assert.Contains("Earned: 🥉 Bookworm", text);
        Assert.DoesNotContain("⏳", text);
    }

    [Fact]
    public async Task Menu_ShouldLinkAchievements_BelowMyProgressAndLeaderboard()
    {
        using var telegramTestHost = GetTelegramTestHost();
        AddUser(telegramTestHost, DefaultUser.Id, "Me");

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Menu)));

        var buttons = Buttons(telegramTestHost.Requests().Last<EditMessageTextRequest>());
        var leaderboardRow = Array.FindIndex(buttons, row => row.Any(b => b.Item2 == "lb"));
        Assert.Equal([("🏅 Achievements", "achs")], buttons[leaderboardRow + 1]);
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
    public async Task MyProgress_ShouldCountAchievements()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id, "Me");
        AddFinishedQuiz(telegramTestHost, me);
        Grant(telegramTestHost, me, Achievement.FirstQuiz, 1);

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Stat)));

        var request = telegramTestHost.Requests().Single<EditMessageTextRequest>();
        Assert.Contains("🏅 Achievements: 1 of 26", request.Text!.Split('\n'));
    }

    private static void AssertFollow(string[] lines, string line, params string[] next)
    {
        var index = Array.IndexOf(lines, line);
        Assert.True(index >= 0, $"No line \"{line}\"");
        Assert.Equal(next, lines.Skip(index + 1).Take(next.Length));
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
