using Laraue.Apps.LearnLanguage.AppServices.Services.Achievements;
using Laraue.Apps.LearnLanguage.AppServices.Services.Leagues;
using Laraue.Apps.LearnLanguage.Contracts.Enums;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Seasons;

/// <summary>
/// What <see cref="SeasonJob"/> has done, kept in the job state.
/// </summary>
public sealed class SeasonJobData
{
    /// <summary>The start of the last week whose trophies were awarded and after which leagues were updated.</summary>
    public DateTime? WeekDone { get; set; }

    /// <summary>The start of the last month whose trophies were awarded.</summary>
    public DateTime? MonthDone { get; set; }
}

/// <summary>
/// Runs daily at 00:00 UTC and closes the seasons that have ended: on the 1st, the month's trophies; on Monday,
/// the week's trophies, then leagues move up (and a check for the 🌳 Rising star achievement). What's done is
/// kept in the job state, so a missed run catches up and a repeated one does nothing. The first run only
/// records where things stand, so a deploy doesn't award past seasons.
/// </summary>
public sealed class SeasonJob(
    ITrophyService trophyService,
    ILeagueUpdater leagueUpdater,
    IAchievementChecker achievementChecker,
    IDateTimeProvider dateTimeProvider,
    ILogger<SeasonJob> logger)
    : BaseJob<SeasonJobData>
{
    public const string Name = "Seasons";

    private static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);

    public override async Task<TimeSpan> ExecuteAsync(
        JobState<SeasonJobData> jobState,
        CancellationToken stoppingToken = default)
    {
        var now = dateTimeProvider.UtcNow;
        try
        {
            await CloseEndedSeasonsAsync(jobState, now, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Closing seasons failed, retrying in {RetryInterval}", RetryInterval);
            return RetryInterval;
        }

        return TimeToNextDay(now);
    }

    private async Task CloseEndedSeasonsAsync(JobState<SeasonJobData> jobState, DateTime now, CancellationToken ct)
    {
        var data = jobState.JobData;
        var lastWeek = Seasons.LastEndedStart(Season.Week, now);
        var lastMonth = Seasons.LastEndedStart(Season.Month, now);

        if (data.WeekDone is null && data.MonthDone is null)
        {
            data.WeekDone = lastWeek;
            data.MonthDone = lastMonth;
            await UpdateStateAsync(jobState, ct);
            return;
        }

        if (data.MonthDone != lastMonth)
        {
            var awarded = await trophyService.AwardAsync(Season.Month, lastMonth, ct);
            logger.LogInformation("Month {Month:yyyy-MM}: {Count} trophies", lastMonth, awarded);
            data.MonthDone = lastMonth;
            await UpdateStateAsync(jobState, ct);
        }

        if (data.WeekDone != lastWeek)
        {
            // Trophies first: the week was played in the leagues as they were.
            var awarded = await trophyService.AwardAsync(Season.Week, lastWeek, ct);
            var movedUp = await leagueUpdater.UpdateAsync(ct);
            foreach (var userId in movedUp)
                await achievementChecker.RequestCheckAsync(userId, ct);
            logger.LogInformation(
                "Week {Week:yyyy-MM-dd}: {Count} trophies, {MovedUp} users moved up",
                lastWeek,
                awarded,
                movedUp.Count);
            data.WeekDone = lastWeek;
            await UpdateStateAsync(jobState, ct);
        }
    }

    /// <summary>
    /// Time from <paramref name="now"/> to the next 00:00 UTC.
    /// </summary>
    public static TimeSpan TimeToNextDay(DateTime now) => now.Date.AddDays(1) - now;
}
