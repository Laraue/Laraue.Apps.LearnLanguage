using System.Text.RegularExpressions;
using Laraue.Apps.LearnLanguage.AppServices;
using Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;
using Laraue.Apps.LearnLanguage.AppServices.Services;
using Laraue.Apps.LearnLanguage.Contracts.Enums;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using Xunit;
using TelegramUser = Telegram.Bot.Types.User;
using User = Laraue.Apps.LearnLanguage.DataAccess.Entities.User;

namespace Laraue.Apps.LearnEnglish.IntegrationTests;

/// <summary>
/// The leaderboard: +3 for a right answer, −1 for a wrong one, names masked unless the user chose what to
/// show (MIN-44).
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
        var alice = AddUser(telegramTestHost, 2, "Alice", nameMode: LeaderboardNameMode.FirstName);
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
            <h3>🏆 Leaderboard · this week</h3>
            <p>+3 for a right answer, −1 for a wrong one</p>
            <table bordered compact>
            <tr><th>#</th><th>Player</th><th align="right">Points</th><th align="right">Correct</th></tr>
            <tr><td align="center">🥇</td><td>Alice</td><td align="right">15</td><td align="right">100%</td></tr>
            <tr><td align="center">🥈</td><td>*******</td><td align="right">12</td><td align="right">50%</td></tr>
            <tr><td align="center">🥉</td><td>******* <b>(you)</b></td><td align="right">8</td><td align="right">75%</td></tr>
            <tr><td align="center">4</td><td>*******</td><td align="right">-2</td><td align="right">17%</td></tr>
            </table>
            <footer>🙈 Your name is hidden from others. You can show it in ⚙️ Settings.</footer>
            """.ReplaceLineEndings("\n"),
            Html(request).TrimEnd());

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
        Assert.StartsWith("<h3>🏆 Leaderboard · this month</h3>", Html(request));
        Assert.Equal(
            ["# | Player | Points | Correct", "🥇 | ******* | 6 | 100%", "🥈 | ******* | 3 | 100%", "🥉 | ******* | 2 | 50%"],
            Rows(request));
        // The viewer hasn't played: no own row, no hint about the name.
        Assert.DoesNotContain("(you)", Html(request));
        Assert.DoesNotContain("🙈", Html(request));
        Assert.Equal("✅ This month", Buttons(request)[0][1].Item1);
    }

    [Fact]
    public async Task Leaderboard_ShouldShowTheViewerBelowTheTop()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id, "Me", nameMode: LeaderboardNameMode.FirstName);
        AddAnswers(telegramTestHost, me, Now, correct: 1, wrong: 0);
        for (var id = 2; id <= 12; id++)
        {
            var user = AddUser(telegramTestHost, id, $"User{id}");
            AddAnswers(telegramTestHost, user, Now, correct: id, wrong: 0);
        }

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Leaderboard)));

        var request = telegramTestHost.Requests().Single<EditMessageTextRequest>();
        var rows = Rows(request);
        Assert.Contains("🥇 | ******* | 36 | 100%", rows);
        Assert.Contains("10 | ******* | 9 | 100%", rows);
        Assert.DoesNotContain(rows, r => r.StartsWith("11 "));
        Assert.Equal(["…", "12 | Me <b>(you)</b> | 3 | 100%"], rows[^2..]);
        // The name is shown, so no hint about hiding it.
        Assert.DoesNotContain("🙈", Html(request));
    }

    [Fact]
    public async Task Leaderboard_ShouldGiveTiesTheSamePlace()
    {
        using var telegramTestHost = GetTelegramTestHost();
        // The largest id, so the viewer is listed last of the tied users.
        var me = AddUser(telegramTestHost, DefaultUser.Id, "Me", nameMode: LeaderboardNameMode.FirstName, id: Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        AddAnswers(telegramTestHost, me, Now, correct: 1, wrong: 0);
        // Ten users ahead of the viewer, the tenth tied with them: the viewer shares 10th place but isn't
        // among the 10 rows, so they're shown below.
        for (var id = 2; id <= 10; id++)
            AddAnswers(telegramTestHost, AddUser(telegramTestHost, id, $"User{id}"), Now, correct: 5, wrong: 0);
        AddAnswers(telegramTestHost, AddUser(telegramTestHost, 11, "Tied"), Now, correct: 1, wrong: 0);
        AddAnswers(telegramTestHost, AddUser(telegramTestHost, 12, "Tied"), Now, correct: 1, wrong: 0);

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Leaderboard)));

        var rows = Rows(telegramTestHost.Requests().Single<EditMessageTextRequest>());
        Assert.Equal(9, rows.Count(r => r == "🥇 | ******* | 15 | 100%"));
        Assert.Contains("10 | ******* | 3 | 100%", rows);
        Assert.Equal(["…", "10 | Me <b>(you)</b> | 3 | 100%"], rows[^2..]);
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

        var request = telegramTestHost.Requests().Single<EditMessageTextRequest>();
        Assert.Contains("<p>Hardly anyone has played yet. Answer a few questions and take the lead!</p>", Html(request));
        // Only the viewer's own row, not the leader's.
        Assert.Equal(["# | Player | Points | Correct", "🥈 | ******* <b>(you)</b> | 6 | 100%"], Rows(request));
    }

    [Fact]
    public async Task Settings_ShouldLetChooseTheName_PreviewingTheCurrentProfile()
    {
        using var telegramTestHost = GetTelegramTestHost();
        // Stored names are old: the library stores the current ones on the user's first update.
        var me = AddUser(telegramTestHost, DefaultUser.Id, "Old");

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Settings)));
        var settings = telegramTestHost.Requests().Single<EditMessageTextRequest>();
        Assert.Contains("🏆 Name in the leaderboard: <b>hidden</b>", settings.Text);
        var nameButton = Buttons(settings).SelectMany(r => r).Single(b => b.Item1 == "🏆 Name in the leaderboard");

        await telegramTestHost.SendUpdateAsync(Callback(nameButton.Item2!));
        var choices = telegramTestHost.Requests().Last<EditMessageTextRequest>();
        Assert.Contains("How should others see you in the leaderboard?", choices.Text);
        Assert.Equal(
            ["✅ 🙈 Hidden", "Me", "Smith", "Me Smith", "@me_smith", "Settings 🔙"],
            Buttons(choices).Select(r => r.Single().Item1).ToArray());

        var usernameButton = Buttons(choices).SelectMany(r => r).Single(b => b.Item1 == "@me_smith");
        await telegramTestHost.SendUpdateAsync(Callback(usernameButton.Item2!));

        Assert.Equal(
            LeaderboardNameMode.Username,
            telegramTestHost.GetFromDb(db => db.Users.Single(u => u.Id == me.Id)).LeaderboardName);
        Assert.Contains(
            "🏆 Name in the leaderboard: <b>@me_smith</b>",
            telegramTestHost.Requests().Last<EditMessageTextRequest>().Text);
    }

    [Fact]
    public async Task Settings_ShouldOfferOnlyTheNamesTheProfileHas()
    {
        using var telegramTestHost = GetTelegramTestHost();
        AddUser(telegramTestHost, DefaultUser.Id, "Me", nameMode: LeaderboardNameMode.FirstName);
        var withoutLastNameAndUsername = new TelegramUser { Id = DefaultUser.Id, FirstName = "Me" };

        await telegramTestHost.SendUpdateAsync(
            Callback(new CallbackRoutePath(TelegramRoutes.LeaderboardNameSettings), withoutLastNameAndUsername));

        var choices = telegramTestHost.Requests().Single<EditMessageTextRequest>();
        Assert.Equal(
            ["🙈 Hidden", "✅ Me", "Settings 🔙"],
            Buttons(choices).Select(r => r.Single().Item1).ToArray());
    }

    [Fact]
    public async Task Profile_ShouldBeUpdatedFromTelegram()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id, "Old", nameMode: LeaderboardNameMode.FullName);

        // Any update: the library asks to store the profile on the first update of a user after a start.
        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Menu)));

        var user = telegramTestHost.GetFromDb(db => db.Users.Single(u => u.Id == me.Id));
        Assert.Equal(("me_smith", "Me", "Smith"), (user.TelegramUserName, user.TelegramFirstName, user.TelegramLastName));
    }

    [Fact]
    public async Task Menu_ShouldLinkTheLeaderboard_BelowMyProgressAndAchievements()
    {
        using var telegramTestHost = GetTelegramTestHost();
        AddUser(telegramTestHost, DefaultUser.Id, "Me");

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Menu)));

        var buttons = Buttons(telegramTestHost.Requests().Last<EditMessageTextRequest>());
        var progressRow = Array.FindIndex(buttons, row => row.SequenceEqual([("📊 My Progress", "stat"), ("🏅 Achievements", "achs")]));
        Assert.True(progressRow >= 0);
        Assert.Equal([("🏆 Leaderboard", "lb")], buttons[progressRow + 1]);
    }

    [Theory]
    [InlineData(LeaderboardNameMode.Hidden, "*******")]
    [InlineData(LeaderboardNameMode.FirstName, "Wilhelm")]
    [InlineData(LeaderboardNameMode.LastName, "&lt;Tell&gt;")]
    [InlineData(LeaderboardNameMode.FullName, "Wilhelm &lt;Tell&gt;")]
    [InlineData(LeaderboardNameMode.Username, "@will")]
    public void LeaderboardNames_ShouldShowTheChosenPartEscaped(LeaderboardNameMode mode, string expected)
    {
        Assert.Equal(expected, LeaderboardNames.Format(mode, new TelegramProfile("will", "Wilhelm", "<Tell>")));
    }

    [Theory]
    [InlineData(LeaderboardNameMode.FirstName)]
    [InlineData(LeaderboardNameMode.LastName)]
    [InlineData(LeaderboardNameMode.Username)]
    public void LeaderboardNames_ShouldMask_WhenTheChosenPartIsMissing(LeaderboardNameMode mode)
    {
        Assert.Equal("*******", LeaderboardNames.Format(mode, new TelegramProfile(null, "  ", null)));
    }

    private static User AddUser(
        AppTelegramTestHost host,
        long telegramId,
        string firstName,
        LeaderboardNameMode nameMode = LeaderboardNameMode.Hidden,
        Guid? id = null)
    {
        var user = new User
        {
            Id = id ?? Guid.NewGuid(),
            TelegramId = telegramId,
            GlobalUserId = Guid.NewGuid(),
            TelegramFirstName = firstName,
            LeaderboardName = nameMode,
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

    // The leaderboard is a rich message (MIN-49): its HTML is in the rich content, not in the text.
    private static string Html(EditMessageTextRequest request)
    {
        Assert.Null(request.Text);
        return Assert.IsType<InputRichMessage>(request.RichMessage).Html!;
    }

    // Table rows as "place | name | points | percent"; the row separating the viewer from the top is "…".
    private static string[] Rows(EditMessageTextRequest request) =>
        Regex.Matches(Html(request), "<tr>(.*?)</tr>")
            .Select(row => string.Join(" | ", Regex.Matches(row.Groups[1].Value, "<t[dh][^>]*>(.*?)</t[dh]>")
                .Select(cell => cell.Groups[1].Value)))
            .ToArray();

    private static (string, string?)[][] Buttons(EditMessageTextRequest request) =>
        ((InlineKeyboardMarkup)request.ReplyMarkup!).InlineKeyboard
            .Select(row => row.Select(b => (b.Text, b.CallbackData)).ToArray())
            .ToArray();

    // The viewer's Telegram account as the updates carry it.
    private static TelegramUser Viewer => new()
    {
        Id = DefaultUser.Id,
        Username = "me_smith",
        FirstName = "Me",
        LastName = "Smith",
    };

    private static Update Callback(string data, TelegramUser? from = null) => new()
    {
        CallbackQuery = new CallbackQuery
        {
            From = from ?? Viewer,
            Data = data,
            Message = new Message { Id = 1 },
        },
    };
}
