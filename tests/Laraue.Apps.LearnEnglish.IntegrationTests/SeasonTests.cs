using System.Globalization;
using Laraue.Apps.LearnLanguage.AppServices;
using Laraue.Apps.LearnLanguage.AppServices.Services.Seasons;
using Laraue.Apps.LearnLanguage.Contracts.Enums;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Extensions.Hosting;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Xunit;
using User = Laraue.Apps.LearnLanguage.DataAccess.Entities.User;

namespace Laraue.Apps.LearnEnglish.IntegrationTests;

/// <summary>
/// Seasonal trophies and the daily season job (MIN-43).
/// </summary>
public class SeasonTests : IntegrationTest
{
    private static readonly long Ru = DefaultContextData.GetWordLanguages().GetId("ru");
    private static readonly DateTime LastWeek = Seasons.LastEndedStart(Season.Week, DateTime.UtcNow);
    private static readonly int LastWeekNumber = ISOWeek.GetWeekOfYear(LastWeek);

    [Fact]
    public async Task Trophies_ShouldGoToTheTop3_TiesSharingAPlace()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var anna = AddUser(telegramTestHost, 1);
        var bob = AddUser(telegramTestHost, 2);
        var carl = AddUser(telegramTestHost, 3);
        var dan = AddUser(telegramTestHost, 4);
        var eve = AddUser(telegramTestHost, 5);
        AddAnswers(telegramTestHost, anna, LastWeek.AddDays(1), correct: 5);
        AddAnswers(telegramTestHost, bob, LastWeek.AddDays(2), correct: 3);
        AddAnswers(telegramTestHost, carl, LastWeek.AddDays(3), correct: 3);
        AddAnswers(telegramTestHost, dan, LastWeek.AddDays(4), correct: 1);
        // No points: no place.
        AddAnswers(telegramTestHost, eve, LastWeek.AddDays(4), correct: 0, wrong: 1);
        // Answers of this week don't count for last week.
        AddAnswers(telegramTestHost, dan, LastWeek.AddDays(7), correct: 100);

        Assert.Equal(3, await AwardAsync(telegramTestHost, Season.Week, LastWeek));

        var trophies = telegramTestHost.GetFromDb(db => db.UserTrophies.ToArray());
        Assert.Equal(
            [(anna.Id, (byte)1), (bob.Id, (byte)2), (carl.Id, (byte)2)],
            trophies.Select(t => (t.UserId, t.Place)).OrderBy(t => t.Place).ThenBy(t => t.UserId == carl.Id).ToArray());
        Assert.All(trophies, t => Assert.Equal((Season.Week, LastWeek), (t.Season, t.SeasonStart)));

        var messages = telegramTestHost.Requests().OfType<SendMessageRequest>().ToArray();
        Assert.Equal(3, messages.Length);
        Assert.Contains(messages, m => m.Text.TrimEnd() == $"<b>🏆 🥇 1st place · week {LastWeekNumber}</b>");

        // Awarding the same season again adds nothing.
        Assert.Equal(0, await AwardAsync(telegramTestHost, Season.Week, LastWeek));
    }

    [Fact]
    public async Task Trophies_ShouldNotBeAwarded_WithFewerThanThreePlayers()
    {
        using var telegramTestHost = GetTelegramTestHost();
        AddAnswers(telegramTestHost, AddUser(telegramTestHost, 1), LastWeek.AddDays(1), correct: 9);
        AddAnswers(telegramTestHost, AddUser(telegramTestHost, 2), LastWeek.AddDays(1), correct: 5);

        Assert.Equal(0, await AwardAsync(telegramTestHost, Season.Week, LastWeek));
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
            AddAnswers(telegramTestHost, users[i], LastWeek.AddDays(1), correct: 3 - i);

        await AwardAsync(telegramTestHost, Season.Week, LastWeek);

        var messages = telegramTestHost.Requests().OfType<SendMessageRequest>().Select(m => m.Text.TrimEnd()).ToArray();
        Assert.Contains($"<b>🏆 🥇 1-е место · неделя {LastWeekNumber}</b>", messages);
        Assert.Contains($"<b>🏆 🥈 2nd place · week {LastWeekNumber}</b>", messages);
    }

    [Fact]
    public async Task SeasonJob_ShouldOnlyRecordOnTheFirstRun_ThenCloseTheEndedSeasons()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var players = Enumerable.Range(1, 3).Select(id => AddUser(telegramTestHost, id)).ToArray();
        foreach (var player in players)
            AddAnswers(telegramTestHost, player, LastWeek.AddDays(1), correct: (int)player.TelegramId);
        var state = new JobState<SeasonJobData> { JobName = SeasonJob.Name };

        // First run: nothing awarded.
        await RunSeasonJobAsync(telegramTestHost, state);
        Assert.Equal(LastWeek, state.JobData.WeekDone);
        Assert.Equal(Seasons.LastEndedStart(Season.Month, DateTime.UtcNow), state.JobData.MonthDone);
        Assert.Empty(telegramTestHost.GetFromDb(db => db.UserTrophies.ToArray()));

        // A run after last week ended.
        state.JobData.WeekDone = LastWeek.AddDays(-7);
        var wait = await RunSeasonJobAsync(telegramTestHost, state);

        Assert.Equal(LastWeek, state.JobData.WeekDone);
        Assert.InRange(wait, TimeSpan.Zero, TimeSpan.FromDays(1));
        Assert.Equal(3, telegramTestHost.GetFromDb(db => db.UserTrophies.ToArray()).Length);

        // Nothing left to do on the same day.
        await RunSeasonJobAsync(telegramTestHost, state);
        Assert.Equal(3, telegramTestHost.GetFromDb(db => db.UserTrophies.ToArray()).Length);
    }

    [Fact]
    public async Task AchievementsList_ShouldShowTheTrophyShelf()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var me = AddUser(telegramTestHost, DefaultUser.Id);
        AddTrophy(telegramTestHost, me, Season.Week, LastWeek, 1);
        AddTrophy(telegramTestHost, me, Season.Week, LastWeek.AddDays(-7), 2);
        AddTrophy(telegramTestHost, me, Season.Month, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), 2);

        await telegramTestHost.SendUpdateAsync(new Update
        {
            CallbackQuery = new CallbackQuery
            {
                From = DefaultUser,
                Data = new CallbackRoutePath(TelegramRoutes.Achievements),
                Message = new Message { Id = 1 },
            },
        });

        Assert.Contains(
            "🏆 Trophies: 🥇 ×1 · 🥈 ×2",
            telegramTestHost.Requests().Single<EditMessageTextRequest>().Text!.Split('\n'));
    }

    [Theory]
    [InlineData("2026-11-11T12:30:00", 11.5)]
    [InlineData("2026-11-11T00:00:00", 24)]
    public void SeasonJob_ShouldRunAgainAtTheNextMidnight(string now, double hours)
    {
        var at = DateTime.SpecifyKind(DateTime.Parse(now), DateTimeKind.Utc);

        Assert.Equal(TimeSpan.FromHours(hours), SeasonJob.TimeToNextDay(at));
    }

    private static async Task<int> AwardAsync(AppTelegramTestHost host, Season season, DateTime start)
    {
        using var scope = host.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITrophyService>().AwardAsync(season, start);
    }

    private static async Task<TimeSpan> RunSeasonJobAsync(AppTelegramTestHost host, JobState<SeasonJobData> state)
    {
        using var scope = host.CreateScope();
        var services = scope.ServiceProvider;
        var job = new SeasonJob(
            services.GetRequiredService<ITrophyService>(),
            services.GetRequiredService<IDateTimeProvider>(),
            NullLogger<SeasonJob>.Instance);
        return await job.ExecuteAsync(state);
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

    private static void AddTrophy(AppTelegramTestHost host, User user, Season season, DateTime start, byte place) =>
        host.InsertIntoDb(new UserTrophy
        {
            UserId = user.Id, Season = season, SeasonStart = start, Place = place,
            AwardedAt = DateTime.UtcNow,
        });
}
