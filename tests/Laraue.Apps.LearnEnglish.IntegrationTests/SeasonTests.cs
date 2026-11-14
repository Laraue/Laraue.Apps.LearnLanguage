using System.Globalization;
using Laraue.Apps.LearnLanguage.AppServices;
using Laraue.Apps.LearnLanguage.AppServices.Services.Seasons;
using Laraue.Apps.LearnLanguage.Contracts.Enums;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Testing;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using Xunit;
using User = Laraue.Apps.LearnLanguage.DataAccess.Entities.User;

namespace Laraue.Apps.LearnEnglish.IntegrationTests;

/// <summary>
/// Seasons and their trophies (MIN-43).
/// </summary>
public class SeasonTests : IntegrationTest
{
    private static readonly long Ru = DefaultContextData.GetWordLanguages().GetId("ru");
    private static readonly DateTime LastMonth = SeasonPeriods.LastEndedStart(SeasonType.Month, DateTime.UtcNow);
    // Only months award trophies, named like "October 2026".
    private static string MonthName(string culture)
    {
        var month = new CultureInfo(culture).DateTimeFormat.GetMonthName(LastMonth.Month);
        return $"{char.ToUpper(month[0], new CultureInfo(culture))}{month[1..]} {LastMonth.Year}";
    }

    [Fact]
    public async Task Trophies_ShouldGoToTheTop3_TiesSharingAPlace()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var anna = AddUser(telegramTestHost, 1);
        var bob = AddUser(telegramTestHost, 2);
        var carl = AddUser(telegramTestHost, 3);
        var dan = AddUser(telegramTestHost, 4);
        var eve = AddUser(telegramTestHost, 5);
        AddAnswers(telegramTestHost, anna, LastMonth.AddDays(1), correct: 5);
        AddAnswers(telegramTestHost, bob, LastMonth.AddDays(2), correct: 3);
        AddAnswers(telegramTestHost, carl, LastMonth.AddDays(3), correct: 3);
        AddAnswers(telegramTestHost, dan, LastMonth.AddDays(4), correct: 1);
        // No points: no place.
        AddAnswers(telegramTestHost, eve, LastMonth.AddDays(4), correct: 0, wrong: 1);
        // Answers of this month don't count for last month.
        AddAnswers(telegramTestHost, dan, LastMonth.AddMonths(1), correct: 100);
        var season = AddSeason(telegramTestHost, SeasonType.Month, LastMonth);

        Assert.Equal(3, await CloseEndedSeasonsAsync(telegramTestHost));

        var trophies = telegramTestHost.GetFromDb(db => db.UserTrophies.ToArray());
        Assert.Equal(
            [(anna.Id, (byte)1), (bob.Id, (byte)2), (carl.Id, (byte)2)],
            trophies.Select(t => (t.UserId, t.Place)).OrderBy(t => t.Place).ThenBy(t => t.UserId == carl.Id).ToArray());
        Assert.All(trophies, t => Assert.Equal(season.Id, t.SeasonId));
        Assert.NotNull(telegramTestHost.GetFromDb(db => db.Seasons.Single(s => s.Id == season.Id)).ClosedAt);

        var messages = telegramTestHost.Requests().OfType<SendMessageRequest>().ToArray();
        Assert.Equal(3, messages.Length);
        Assert.Contains(messages, m => m.Text.TrimEnd() == $"<b>🏆 🥇 1st place · {MonthName("en")}</b>");
        var share = ((InlineKeyboardMarkup)messages[0].ReplyMarkup!).InlineKeyboard.First().Last().Url!;
        Assert.Equal(
            "https://t.me/TestBot?start=source-trophy",
            System.Web.HttpUtility.ParseQueryString(new Uri(share).Query)["url"]);

        // A closed season isn't awarded again.
        Assert.Equal(0, await CloseEndedSeasonsAsync(telegramTestHost));
    }

    [Fact]
    public async Task Season_ShouldBeClosedWithoutTrophies_WithFewerThanThreePlayers()
    {
        using var telegramTestHost = GetTelegramTestHost();
        AddAnswers(telegramTestHost, AddUser(telegramTestHost, 1), LastMonth.AddDays(1), correct: 9);
        AddAnswers(telegramTestHost, AddUser(telegramTestHost, 2), LastMonth.AddDays(1), correct: 5);
        var season = AddSeason(telegramTestHost, SeasonType.Month, LastMonth);

        Assert.Equal(0, await CloseEndedSeasonsAsync(telegramTestHost));
        Assert.NotNull(telegramTestHost.GetFromDb(db => db.Seasons.Single(s => s.Id == season.Id)).ClosedAt);
        Assert.Empty(telegramTestHost.Requests().OfType<SendMessageRequest>());
    }

    [Fact]
    public async Task TrophyMessage_ShouldBeInTheUsersLanguage()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var users = new[]
        {
            AddUser(telegramTestHost, 1, languageCode: "ru"),
            AddUser(telegramTestHost, 2),
            AddUser(telegramTestHost, 3),
        };
        for (var i = 0; i < users.Length; i++)
            AddAnswers(telegramTestHost, users[i], LastMonth.AddDays(1), correct: 3 - i);
        AddSeason(telegramTestHost, SeasonType.Month, LastMonth);

        await CloseEndedSeasonsAsync(telegramTestHost);

        var messages = telegramTestHost.Requests().OfType<SendMessageRequest>().Select(m => m.Text.TrimEnd()).ToArray();
        Assert.Contains($"<b>🏆 🥇 1-е место · {MonthName("ru")}</b>", messages);
        Assert.Contains($"<b>🏆 🥈 2nd place · {MonthName("en")}</b>", messages);
    }

    [Fact]
    public async Task FirstRun_ShouldRecordEndedSeasonsAsClosed_WithoutTrophies()
    {
        using var telegramTestHost = GetTelegramTestHost();
        foreach (var id in Enumerable.Range(1, 3))
            AddAnswers(telegramTestHost, AddUser(telegramTestHost, id), LastMonth.AddDays(1), correct: id);

        Assert.Equal(0, await CloseEndedSeasonsAsync(telegramTestHost));

        var seasons = telegramTestHost.GetFromDb(db => db.Seasons.OrderBy(s => s.Type).ToArray());
        Assert.Equal(
            [(SeasonType.Month, LastMonth)],
            seasons.Select(s => (s.Type, s.StartsAt)).ToArray());
        Assert.All(seasons, s => Assert.NotNull(s.ClosedAt));
        Assert.Empty(telegramTestHost.GetFromDb(db => db.UserTrophies.ToArray()));
    }

    [Fact]
    public async Task NextRun_ShouldRecordTheEndedMonth_AndAwardIt()
    {
        using var telegramTestHost = GetTelegramTestHost();
        foreach (var id in Enumerable.Range(1, 3))
            AddAnswers(telegramTestHost, AddUser(telegramTestHost, id), LastMonth.AddDays(1), correct: id);
        // Seasons were recorded before: the month before last is closed.
        AddSeason(telegramTestHost, SeasonType.Month, LastMonth.AddMonths(-1), closed: true);

        Assert.Equal(3, await CloseEndedSeasonsAsync(telegramTestHost));

        var lastWeek = telegramTestHost.GetFromDb(db => db.Seasons
            .Single(s => s.Type == SeasonType.Month && s.StartsAt == LastMonth));
        Assert.NotNull(lastWeek.ClosedAt);
        Assert.Equal(LastMonth.AddMonths(1), lastWeek.EndsAt);
    }

    [Fact]
    public async Task AchievementsList_ShouldShowTheTrophyShelf()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id);
        AddTrophy(telegramTestHost, me, AddSeason(telegramTestHost, SeasonType.Week, LastMonth, closed: true), 1);
        AddTrophy(telegramTestHost, me, AddSeason(telegramTestHost, SeasonType.Week, LastMonth.AddDays(-7), closed: true), 2);
        AddTrophy(telegramTestHost, me, AddSeason(telegramTestHost, SeasonType.Month,
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), closed: true), 2);

        await telegramTestHost.SendUpdateAsync(new Update
        {
            CallbackQuery = new CallbackQuery
            {
                From = DefaultUser,
                Data = new CallbackRoutePath(TelegramRoutes.Achievements),
                Message = new Message { Id = 1 },
            },
        });

        // The achievements screen is a rich message (MIN-49).
        Assert.Contains(
            "<p>🏆 Trophies: 🥇 ×1 · 🥈 ×2</p>",
            telegramTestHost.Requests().Single<EditMessageTextRequest>().RichMessage!.Html);
    }

    [Theory]
    [InlineData("2026-11-11T12:30:00", 11.5)]
    [InlineData("2026-11-11T00:00:00", 24)]
    public void SeasonJob_ShouldRunAgainAtTheNextMidnight(string now, double hours)
    {
        var at = DateTime.SpecifyKind(DateTime.Parse(now), DateTimeKind.Utc);

        Assert.Equal(TimeSpan.FromHours(hours), SeasonJob.TimeToNextDay(at));
    }

    private static async Task<int> CloseEndedSeasonsAsync(AppTelegramTestHost host)
    {
        using var scope = host.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISeasonService>().CloseEndedSeasonsAsync();
    }

    private static Season AddSeason(AppTelegramTestHost host, SeasonType type, DateTime startsAt, bool closed = false)
    {
        var season = new Season
        {
            Type = type,
            StartsAt = startsAt,
            EndsAt = SeasonPeriods.EndOf(type, startsAt),
            ClosedAt = closed ? startsAt : null,
        };
        host.InsertIntoDb(season);
        return season;
    }

    private static User AddUser(AppTelegramTestHost host, long telegramId, string? languageCode = null)
    {
        var user = new User
        {
            TelegramId = telegramId,
            GlobalUserId = Guid.NewGuid(),
            LanguageToLearnId = Ru,
            TelegramLanguageCode = languageCode,
        };
        host.InsertIntoDb(user);
        return user;
    }

    private static void AddAnswers(AppTelegramTestHost host, User user, DateTime at, int correct, int wrong = 0)
    {
        var wordId = host.GetFromDb(db => db.Words.OrderBy(w => w.Id).First()).Id;
        host.InsertIntoDb(new UserQuiz
        {
            UserId = user.Id,
            LanguageId = Ru,
            Status = UserQuizStatus.Finished,
            CreatedAt = at,
            FinishedAt = at,
            UserQuizQuestions = Enumerable.Repeat(UserQuizQuestionStatus.Correct, correct)
                .Concat(Enumerable.Repeat(UserQuizQuestionStatus.Incorrect, wrong))
                .Select(s => new UserQuizQuestion { WordId = wordId, Status = s, AnsweredAt = at })
                .ToList(),
        });
    }

    private static void AddTrophy(AppTelegramTestHost host, User user, Season season, byte place) =>
        host.InsertIntoDb(new UserTrophy
        {
            UserId = user.Id, SeasonId = season.Id, Place = place, AwardedAt = DateTime.UtcNow,
        });
}
