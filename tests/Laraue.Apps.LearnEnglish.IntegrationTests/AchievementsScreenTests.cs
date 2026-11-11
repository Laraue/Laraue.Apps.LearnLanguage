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
        // The level held with what it means, then the next one with the progress to it.
        AssertFollow(lines, "<b>Words learned</b>: 🥉 Bookworm (100 words learned)",
            "Next: 🥈 Word collector · 120 of 500 words");
        AssertFollow(lines, "<b>Without mistakes</b>: 🥉 Sharp eye (a quiz without mistakes)",
            "Next: 🥈 Sniper · 1 of 5 quizzes in a row without mistakes");
        // Every level held: no next one.
        AssertFollow(lines, "<b>Language pairs</b>: 🥈 Polyglot (words learned in 5 language pairs)", "");
        // Earned single achievements, separated by blank lines.
        AssertFollow(lines, "✅ <b>First steps</b> (first quiz finished)", "", "✅ <b>Pioneer</b> (first C1 word learned)");
        // Locked ones are only counted: quizzes finished, levels completed, days in a row and 3 single ones.
        Assert.DoesNotContain(lines, l => l.Contains("Quizzes finished") || l.Contains("Marathon"));
        Assert.Contains("🔒 Still locked: 6", lines);
        // Everything reached is granted: no waiting.
        Assert.DoesNotContain(lines, l => l.StartsWith("⏳"));

        Assert.Equal(("🔒 Show locked", "achs/l"), Buttons(request)[0][0]);
        Assert.Equal(("🎖 Choose a title", "achs/t"), Buttons(request)[1][0]);
        Assert.Equal(("📊 My Progress", "stat"), Buttons(request)[2][0]);
    }

    [Fact]
    public async Task Locked_ShouldListWhatIsNotEarnedYet()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id, "Me");
        AddFinishedQuiz(telegramTestHost, me);
        Grant(telegramTestHost, me, Achievement.FirstQuiz, 1);
        Grant(telegramTestHost, me, Achievement.WithoutMistakes, 1);

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.AchievementsLocked)));

        var request = telegramTestHost.Requests().Single<EditMessageTextRequest>();
        var lines = request.Text!.Split('\n');
        Assert.Equal("<b>🔒 Locked achievements</b>", lines[0]);
        AssertFollow(lines, "<b>Quizzes finished</b>: 🔒", "Next: 🥉 Persistence · 1 of 100 quizzes");
        AssertFollow(lines, "<b>Levels completed</b>: 🔒", "Next: 🥉 A1 Beginner · learn every A1 word");
        Assert.Contains("🔒 <b>Marathon</b> (10 quizzes in one day)", lines);
        // Earned ones aren't here.
        Assert.DoesNotContain(lines, l => l.Contains("First steps") || l.Contains("Without mistakes"));
        Assert.Equal(("← Back", "achs"), Buttons(request).Last()[0]);
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
            "⏳ New achievements are on their way: open this section again after the message about them arrives.",
            telegramTestHost.Requests().Single<EditMessageTextRequest>().Text!.Split('\n'));
        Assert.NotNull(telegramTestHost.GetFromDb(db => db.Users.Single(u => u.Id == me.Id)).AchievementsCheckRequestedAt);

        // The achievements job grants them; the screen no longer waits.
        using (var scope = telegramTestHost.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IAchievementChecker>().CheckRequestedAsync();
        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Achievements)));

        var text = telegramTestHost.Requests().Last<EditMessageTextRequest>().Text!;
        Assert.Contains("<b>Words learned</b>: 🥉 Bookworm", text);
        Assert.DoesNotContain("⏳", text);
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
            ["🥉 Spark (7 days in a row)", "🥈 Flame (30 days in a row)", "✅ Without a title", "← Back"],
            Buttons(titles).Select(r => r.Single().Item1).ToArray());

        var flame = Buttons(titles).SelectMany(r => r).Single(b => b.Item1 == "🥈 Flame (30 days in a row)").Item2!;
        await telegramTestHost.SendUpdateAsync(Callback(flame));

        var user = telegramTestHost.GetFromDb(db => db.Users.Single(u => u.Id == me.Id));
        Assert.Equal((Achievement.DaysInARow, (byte)2), (user.TitleAchievement!.Value, user.TitleLevel!.Value));
        Assert.Contains(
            Buttons(telegramTestHost.Requests().Last<EditMessageTextRequest>()).SelectMany(r => r),
            b => b.Item1 == "🎖 Title: 🔥 Flame");

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
