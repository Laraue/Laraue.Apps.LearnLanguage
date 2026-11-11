using Laraue.Apps.LearnLanguage.AppServices;
using Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;
using Laraue.Apps.LearnLanguage.AppServices.Services;
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
            <b>🏆 Leaderboard · this week</b>
            🌱 Beginner league · words left to 🌳 Intermediate: 1,000
            +3 for a right answer, −1 for a wrong one

            🥇 Alice · 15 pts · 100% correct
            🥈 ******* · 12 pts · 50% correct
            🥉 ******* <b>(you)</b> · 8 pts · 75% correct
            4. ******* · -2 pts · 17% correct

            🙈 Your name is hidden from others. You can show it in ⚙️ Settings.
            """.ReplaceLineEndings("\n"),
            request.Text!.TrimEnd());

        var buttons = Buttons(request);
        Assert.Equal("✅ This week", buttons[0][0].Item1);
        Assert.Equal("This month", buttons[0][1].Item1);
        Assert.Equal(("📊 My Progress", "stat"), buttons[1][0]);
    }

    [Fact]
    public async Task Leaderboard_ShouldShowOnlyTheViewersLeague()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var (ru, _) = Languages();
        var words = WordsOfBothPairs(telegramTestHost, 1000);
        var beforeWeek = WeekStart.AddDays(-1);

        var me = AddUser(telegramTestHost, DefaultUser.Id, "Me", nameMode: LeaderboardNameMode.FirstName);
        var anna = AddUser(telegramTestHost, 2, "Anna", nameMode: LeaderboardNameMode.FirstName);
        var olga = AddUser(telegramTestHost, 3, "Olga", nameMode: LeaderboardNameMode.FirstName);
        var bob = AddUser(telegramTestHost, 4, "Bob", nameMode: LeaderboardNameMode.FirstName);
        var dan = AddUser(telegramTestHost, 5, "Dan", nameMode: LeaderboardNameMode.FirstName);
        foreach (var user in new[] { me, anna, olga })
            AddLearnedWords(telegramTestHost, user, ru, words, beforeWeek);
        // Learned the 1,000th word just now: the league is live, so already Intermediate.
        AddLearnedWords(telegramTestHost, dan, ru, words[..999], beforeWeek);
        AddLearnedWords(telegramTestHost, dan, ru, words[999..], Now);
        // Had 1,000 words, but answered one wrong since, so it's not learned anymore: Beginner.
        AddLearnedWords(telegramTestHost, bob, ru, words[..999], beforeWeek);
        AddLearnedWords(telegramTestHost, bob, ru, words[999..], learnedAt: null);
        foreach (var user in new[] { me, anna, olga, bob, dan })
            AddAnswers(telegramTestHost, user, Now, correct: 1, wrong: 0);

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Leaderboard)));

        var text = telegramTestHost.Requests().Single<EditMessageTextRequest>().Text!;
        Assert.Contains("🌳 Intermediate league", text);
        Assert.Contains("Anna · 3 pts", text);
        Assert.Contains("Olga · 3 pts", text);
        Assert.Contains("Me <b>(you)</b> · 3 pts", text);
        Assert.Contains("Dan · 3 pts", text);
        Assert.DoesNotContain("Bob", text);
    }

    [Fact]
    public async Task Leaderboard_ShouldCountAWordLearnedInTwoPairsOnce()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var (ru, de) = Languages();
        var words = WordsOfBothPairs(telegramTestHost, 999);
        var me = AddUser(telegramTestHost, DefaultUser.Id, "Me");
        AddLearnedWords(telegramTestHost, me, ru, words[..600], WeekStart.AddDays(-1));
        AddLearnedWords(telegramTestHost, me, de, words, WeekStart.AddDays(-1));

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Leaderboard)));

        Assert.Contains(
            "🌱 Beginner league · words left to 🌳 Intermediate: 1",
            telegramTestHost.Requests().Single<EditMessageTextRequest>().Text!.Split('\n'));
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
        Assert.Contains("🥇 ******* · 6 pts · 100% correct", request.Text);
        Assert.Contains("🥈 ******* · 3 pts · 100% correct", request.Text);
        Assert.Contains("🥉 ******* · 2 pts · 50% correct", request.Text);
        // The viewer hasn't played: no own row, no hint about the name.
        Assert.DoesNotContain("(you)", request.Text);
        Assert.DoesNotContain("🙈", request.Text);
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

        var lines = telegramTestHost.Requests().Single<EditMessageTextRequest>().Text!.Split('\n');
        Assert.Contains("🥇 ******* · 36 pts · 100% correct", lines);
        Assert.Contains("10. ******* · 9 pts · 100% correct", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("11."));
        var dots = Array.IndexOf(lines, "…");
        Assert.Equal("12. Me <b>(you)</b> · 3 pts · 100% correct", lines[dots + 1]);
        // The name is shown, so no hint about hiding it.
        Assert.DoesNotContain(lines, l => l.StartsWith("🙈"));
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

        var lines = telegramTestHost.Requests().Single<EditMessageTextRequest>().Text!.Split('\n');
        Assert.Equal(9, lines.Count(l => l.StartsWith("🥇 ******* · 15 pts")));
        Assert.Contains("10. ******* · 3 pts · 100% correct", lines);
        var dots = Array.IndexOf(lines, "…");
        Assert.Equal("10. Me <b>(you)</b> · 3 pts · 100% correct", lines[dots + 1]);
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
        Assert.Contains("🥈 ******* <b>(you)</b> · 6 pts · 100% correct", text);
        Assert.DoesNotContain("🥇", text);
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
    public async Task Menu_ShouldLinkTheLeaderboard_NextToMyProgress()
    {
        using var telegramTestHost = GetTelegramTestHost();
        AddUser(telegramTestHost, DefaultUser.Id, "Me");

        await telegramTestHost.SendUpdateAsync(Callback(new CallbackRoutePath(TelegramRoutes.Menu)));

        var buttons = Buttons(telegramTestHost.Requests().Last<EditMessageTextRequest>());
        Assert.Contains(buttons, row => row.SequenceEqual([("📊 My Progress", "stat"), ("🏆 Leaderboard", "lb")]));
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

    private static (long Ru, long De) Languages()
    {
        var languages = DefaultContextData.GetWordLanguages();
        return (languages.GetId("ru"), languages.GetId("de"));
    }

    // Words translated to both Russian and German, so they can be learned in both pairs.
    private static long[] WordsOfBothPairs(AppTelegramTestHost host, int count)
    {
        var (ru, de) = Languages();
        return host.GetFromDb(db => db.Translations
            .Where(t => t.LanguageId == ru)
            .Select(t => t.WordId)
            .Intersect(db.Translations.Where(t => t.LanguageId == de).Select(t => t.WordId))
            .OrderBy(id => id)
            .Take(count)
            .ToArray());
    }

    private static void AddLearnedWords(
        AppTelegramTestHost host,
        User user,
        long languageId,
        IEnumerable<long> wordIds,
        DateTime? learnedAt)
    {
        host.InsertRangeIntoDb(wordIds.Select(id => new LearnedTranslation
        {
            UserId = user.Id,
            WordId = id,
            LanguageId = languageId,
            WinStreakCount = learnedAt is null ? 0 : 3,
            LearnedAt = learnedAt,
        }));
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
