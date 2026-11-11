using Laraue.Apps.LearnLanguage.AppServices;
using Laraue.Apps.LearnLanguage.AppServices.Services;
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
/// The leaderboard: +3 for a right answer, −1 for a wrong one, names masked unless shown (MIN-44).
/// </summary>
public class LeaderboardTests : IntegrationTest
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static DateTime WeekStart
    {
        get
        {
            var today = DateOnly.FromDateTime(Now);
            return today.AddDays(-((7 + (int)today.DayOfWeek - (int)DayOfWeek.Monday) % 7))
                .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        }
    }

    [Fact]
    public async Task Leaderboard_ShouldRankThisWeekByPoints()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id, "Me");
        var alice = AddUser(telegramTestHost, 2, "Alice", showName: true);
        var bob = AddUser(telegramTestHost, 3, "Bob");
        var carl = AddUser(telegramTestHost, 4, "Carl");

        AddAnswers(telegramTestHost, alice, Now, correct: 5, wrong: 0);
        // More right answers than Alice, but as many wrong ones: fewer points.
        AddAnswers(telegramTestHost, bob, Now, correct: 6, wrong: 6);
        AddAnswers(telegramTestHost, carl, Now, correct: 1, wrong: 5);
        // Carl's answers from before Monday don't count this week.
        AddAnswers(telegramTestHost, carl, WeekStart.AddMinutes(-1), correct: 50, wrong: 0);
        AddAnswers(telegramTestHost, me, Now, correct: 3, wrong: 1, skipped: 4);

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Leaderboard)));

        var request = telegramTestHost.Requests().Single<EditMessageTextRequest>();
        Assert.Equal(
            """
            <b>🏆 Leaderboard · this week</b>
            +3 for a right answer, −1 for a wrong one

            🥇 Alice · 15 pts · 100% correct
            🥈 Bo******* · 12 pts · 50% correct
            🥉 M******* <b>(you)</b> · 8 pts · 75% correct
            4. Ca******* · -2 pts · 17% correct

            🙈 Your name is hidden from others. You can show it in ⚙️ Settings.
            """.ReplaceLineEndings("\n"),
            request.Text.TrimEnd());

        var buttons = Buttons(request);
        Assert.Equal("✅ This week", buttons[0][0].Item1);
        Assert.Equal("This month", buttons[0][1].Item1);
        Assert.Equal(("📊 My Progress", "stat"), buttons[1][0]);
    }

    [Fact]
    public async Task Leaderboard_ShouldCountTheWholeMonth_WhenMonthIsSelected()
    {
        using var telegramTestHost = GetTelegramTestHost();
        AddUser(telegramTestHost, DefaultUser.Id, "Me");
        var users = Enumerable.Range(2, 3).Select(id => AddUser(telegramTestHost, id, $"User{id}")).ToArray();
        var monthStart = new DateTime(Now.Year, Now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        AddAnswers(telegramTestHost, users[0], monthStart, correct: 2, wrong: 0);
        AddAnswers(telegramTestHost, users[0], monthStart.AddMinutes(-1), correct: 100, wrong: 0);
        AddAnswers(telegramTestHost, users[1], Now, correct: 1, wrong: 0);
        AddAnswers(telegramTestHost, users[2], Now, correct: 1, wrong: 1);

        // The month button of the week board, to check the period goes through the callback.
        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Leaderboard)));
        var monthCallback = Buttons(telegramTestHost.Requests().Single<EditMessageTextRequest>())[0][1].Item2!;
        await telegramTestHost.SendUpdateAsync(Callback(monthCallback));

        var request = telegramTestHost.Requests().Last<EditMessageTextRequest>();
        Assert.StartsWith("<b>🏆 Leaderboard · this month</b>", request.Text);
        Assert.Contains("🥇 Us******* · 6 pts · 100% correct", request.Text);
        Assert.Contains("🥈 Us******* · 3 pts · 100% correct", request.Text);
        Assert.Contains("🥉 Us******* · 2 pts · 50% correct", request.Text);
        // The viewer hasn't played: no own row, no hint about the name.
        Assert.DoesNotContain("(you)", request.Text);
        Assert.DoesNotContain("🙈", request.Text);
        Assert.Equal("✅ This month", Buttons(request)[0][1].Item1);
    }

    [Fact]
    public async Task Leaderboard_ShouldShowTheViewerBelowTheTop()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id, "Me", showName: true);
        AddAnswers(telegramTestHost, me, Now, correct: 1, wrong: 0);
        for (var id = 2; id <= 12; id++)
        {
            var user = AddUser(telegramTestHost, id, $"User{id}");
            AddAnswers(telegramTestHost, user, Now, correct: id, wrong: 0);
        }

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Leaderboard)));

        var lines = telegramTestHost.Requests().Single<EditMessageTextRequest>().Text.Split('\n');
        Assert.Contains("🥇 Us******* · 36 pts · 100% correct", lines);
        Assert.Contains("10. Us******* · 9 pts · 100% correct", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("11. Us"));
        var dots = Array.IndexOf(lines, "…");
        Assert.Equal("12. Me <b>(you)</b> · 3 pts · 100% correct", lines[dots + 1]);
        // The name is shown, so no hint about hiding it.
        Assert.DoesNotContain(lines, l => l.StartsWith("🙈"));
    }

    [Fact]
    public async Task Leaderboard_ShouldInviteToPlay_WhenFewPlayers()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id, "Me");
        var other = AddUser(telegramTestHost, 2, "Other");
        AddAnswers(telegramTestHost, me, Now, correct: 2, wrong: 0);
        AddAnswers(telegramTestHost, other, Now, correct: 9, wrong: 0);

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Leaderboard)));

        var text = telegramTestHost.Requests().Single<EditMessageTextRequest>().Text;
        Assert.Contains("Hardly anyone has played yet. Answer a few questions and take the lead!", text);
        Assert.Contains("🥈 M******* <b>(you)</b> · 6 pts · 100% correct", text);
        Assert.DoesNotContain("Ot*******", text);
    }

    [Fact]
    public async Task Settings_ShouldShowAndHideTheNameOnTheLeaderboard()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id, "Me");

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Settings)));
        var settings = telegramTestHost.Requests().Single<EditMessageTextRequest>();
        Assert.Contains("🏆 Name in the leaderboard: <b>hidden</b>", settings.Text);
        var showButton = Buttons(settings).SelectMany(r => r).Single(b => b.Item1 == "🏆 Show my name in the leaderboard");

        await telegramTestHost.SendUpdateAsync(Callback(showButton.Item2!));

        Assert.True(telegramTestHost.GetFromDb(db => db.Users.Single(u => u.Id == me.Id)).ShowNameOnLeaderboard);
        var updated = telegramTestHost.Requests().Last<EditMessageTextRequest>();
        Assert.Contains("🏆 Name in the leaderboard: <b>shown</b>", updated.Text);
        Assert.Contains(Buttons(updated).SelectMany(r => r), b => b.Item1 == "🙈 Hide my name in the leaderboard");
    }

    [Fact]
    public async Task Stats_ShouldLinkTheLeaderboard()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id, "Me");
        AddAnswers(telegramTestHost, me, Now, correct: 1, wrong: 0);

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Stat)));

        var buttons = Buttons(telegramTestHost.Requests().Single<EditMessageTextRequest>());
        Assert.Equal("📤 Share my progress", buttons[1][0].Item1);
        Assert.Equal(("🏆 Leaderboard", "lb"), buttons[1][1]);
    }

    [Theory]
    [InlineData("Wilhelm", false, "Wi*******")]
    [InlineData("Wilhelm", true, "Wilhelm")]
    [InlineData("Ann", false, "An*******")]
    [InlineData("Al", false, "A*******")]
    [InlineData("W", false, "*******")]
    [InlineData("Al", true, "Al")]
    [InlineData("<b>", true, "&lt;b&gt;")]
    [InlineData("<b>", false, "&lt;b*******")]
    [InlineData("👨‍👩‍👧Ann", false, "👨‍👩‍👧A*******")]
    [InlineData(null, true, "*******")]
    [InlineData("  ", false, "*******")]
    public void LeaderboardNames_ShouldMaskAndEncode(string? firstName, bool show, string expected)
    {
        Assert.Equal(expected, LeaderboardNames.Format(firstName, show));
    }

    private static User AddUser(AppTelegramTestHost host, long telegramId, string firstName, bool showName = false)
    {
        var user = new User
        {
            TelegramId = telegramId,
            GlobalUserId = Guid.NewGuid(),
            TelegramFirstName = firstName,
            ShowNameOnLeaderboard = showName,
        };
        host.InsertIntoDb(user);
        return user;
    }

    private static void AddAnswers(
        AppTelegramTestHost host,
        User user,
        DateTime at,
        int correct,
        int wrong,
        int skipped = 0)
    {
        var languageId = DefaultContextData.GetWordLanguages().GetId("ru");
        var wordId = host.GetFromDb(db => db.Words.OrderBy(w => w.Id).First()).Id;
        var statuses = Enumerable.Repeat(UserQuizQuestionStatus.Correct, correct)
            .Concat(Enumerable.Repeat(UserQuizQuestionStatus.Incorrect, wrong))
            .Concat(Enumerable.Repeat(UserQuizQuestionStatus.Skipped, skipped));
        host.InsertIntoDb(new UserQuiz
        {
            UserId = user.Id,
            LanguageId = languageId,
            Status = UserQuizStatus.Finished,
            CreatedAt = at,
            FinishedAt = at,
            UserQuizQuestions = statuses
                .Select(s => new UserQuizQuestion { WordId = wordId, Status = s, AnsweredAt = at })
                .ToList(),
        });
    }

    private static (string, string?)[][] Buttons(EditMessageTextRequest request) =>
        ((InlineKeyboardMarkup)request.ReplyMarkup!).InlineKeyboard
            .Select(row => row.Select(b => (b.Text, b.CallbackData)).ToArray())
            .ToArray();

    private static Update Callback(string data) => new()
    {
        CallbackQuery = new CallbackQuery
        {
            From = DefaultUser,
            Data = data,
            Message = new Message { Id = 1 },
        },
    };
}
