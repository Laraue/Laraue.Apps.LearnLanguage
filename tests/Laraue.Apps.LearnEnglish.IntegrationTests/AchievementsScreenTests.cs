using System.Text.RegularExpressions;
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
        var html = Html(request);
        Assert.StartsWith("<h3>🏅 Achievements · 6 of 26</h3>\n", html);
        // The level held with what it means, then the next one's medal with the progress to it; achievements with
        // levels first, then the earned single ones.
        Assert.Equal(
            [
                "Achievement | Level | Next",
                "📚 Words learned | 🥉 Bookworm (100 words learned) | 🥈 120 / 500",
                "🎯 Without mistakes | 🥉 Sharp eye (a quiz without mistakes) | 🥈 1 / 5",
                // Every level held: no next one.
                "🌍 Language pairs | 🥈 Polyglot (words learned with translations into 5 languages) | –",
                "👣 First steps | ✅ First quiz finished | –",
                "🔤 Pioneer | ✅ First C1 word learned | –",
            ],
            Rows(request));
        // Locked ones are only counted: quizzes finished, levels completed, days in a row and 3 single ones.
        Assert.EndsWith("<footer>🔒 Still locked: 6</footer>", html.TrimEnd());
        // Everything reached is granted: no waiting.
        Assert.DoesNotContain("⏳", html);

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
        Assert.StartsWith("<h3>🔒 Locked achievements</h3>\n", Html(request));
        var rows = Rows(request);
        // Before a first level, what it takes.
        Assert.Contains("🧩 Quizzes finished | 🔒 100 quizzes finished | 🥉 1 / 100", rows);
        Assert.Contains("🔥 Days in a row | 🔒 A quiz every day, 7 days in a row | 🥉 1 / 7", rows);
        // Progress that isn't a count keeps its own wording.
        Assert.Contains("🎓 Levels completed | 🔒 Every A1 word learned | 🥉 learn every A1 word", rows);
        Assert.Contains("⚡ Marathon | 🔒 10 quizzes in one day | –", rows);
        // Earned ones aren't here.
        Assert.DoesNotContain(rows, r => r.Contains("First steps") || r.Contains("Without mistakes"));
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
            "<p>⏳ New achievements are on their way: open this section again after the message about them arrives.</p>",
            Html(telegramTestHost.Requests().Single<EditMessageTextRequest>()));
        Assert.NotNull(telegramTestHost.GetFromDb(db => db.Users.Single(u => u.Id == me.Id)).AchievementsCheckRequestedAt);

        // The achievements job grants them; the screen no longer waits.
        using (var scope = telegramTestHost.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IAchievementChecker>().CheckRequestedAsync();
        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Achievements)));

        var request = telegramTestHost.Requests().Last<EditMessageTextRequest>();
        Assert.Contains("📚 Words learned | 🥉 Bookworm (100 words learned) | 🥈 120 / 500", Rows(request));
        Assert.DoesNotContain("⏳", Html(request));
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
            ["🥉 Spark (a quiz every day, 7 days in a row)", "🥈 Flame (a quiz every day, 30 days in a row)", "✅ Without a title", "← Back"],
            Buttons(titles).Select(r => r.Single().Item1).ToArray());

        var flame = Buttons(titles).SelectMany(r => r).Single(b => b.Item1 == "🥈 Flame (a quiz every day, 30 days in a row)").Item2!;
        await telegramTestHost.SendUpdateAsync(Callback(flame));

        var user = telegramTestHost.GetFromDb(db => db.Users.Single(u => u.Id == me.Id));
        Assert.Equal((Achievement.DaysInARow, (byte)2), (user.TitleAchievement!.Value, user.TitleLevel!.Value));
        Assert.Contains(
            Buttons(telegramTestHost.Requests().Last<EditMessageTextRequest>()).SelectMany(r => r),
            b => b.Item1 == "🎖 Title: 🔥 Flame");

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Leaderboard)));
        // The leaderboard is a rich message with a table (MIN-49): the title follows the name in the player cell.
        Assert.Contains(
            "<td>Me <b>(you)</b> · 🔥 Flame</td><td align=\"right\">3</td>",
            telegramTestHost.Requests().Last<EditMessageTextRequest>().RichMessage!.Html);
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

    // The achievements screens are rich messages (MIN-49): their HTML is in the rich content, not in the text.
    private static string Html(EditMessageTextRequest request)
    {
        Assert.Null(request.Text);
        return Assert.IsType<InputRichMessage>(request.RichMessage).Html!;
    }

    // Table rows as "achievement | level | next".
    private static string[] Rows(EditMessageTextRequest request) =>
        Regex.Matches(Html(request), "<tr>(.*?)</tr>")
            .Select(row => string.Join(" | ", Regex.Matches(row.Groups[1].Value, "<t[dh][^>]*>(.*?)</t[dh]>")
                .Select(cell => cell.Groups[1].Value)))
            .ToArray();

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
