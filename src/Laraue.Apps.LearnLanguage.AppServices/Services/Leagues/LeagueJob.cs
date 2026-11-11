using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Leagues;

/// <summary>
/// Updates leagues once a week, at Monday 00:00 UTC when the week leaderboard starts over, so a league stays
/// the same for the whole week. The job runner stores the next run in the database: after a restart the job
/// waits for it, or runs right away when it was missed.
/// </summary>
public sealed class LeagueJob(
    ILeagueUpdater leagueUpdater,
    IDateTimeProvider dateTimeProvider,
    ILogger<LeagueJob> logger)
    : BaseJob
{
    public const string Name = "UpdateLeagues";

    private static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);

    public override async Task<TimeSpan> ExecuteAsync(
        JobState<EmptyJobData> jobState,
        CancellationToken stoppingToken = default)
    {
        try
        {
            var movedUp = await leagueUpdater.UpdateAsync(stoppingToken);
            logger.LogInformation("Leagues updated, {Count} users moved up", movedUp);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Updating leagues failed, retrying in {RetryInterval}", RetryInterval);
            return RetryInterval;
        }

        return TimeToNextWeek(dateTimeProvider.UtcNow);
    }

    /// <summary>
    /// Time from <paramref name="now"/> to the next Monday 00:00 UTC.
    /// </summary>
    public static TimeSpan TimeToNextWeek(DateTime now)
    {
        var daysToMonday = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
        var nextMonday = now.Date.AddDays(daysToMonday == 0 ? 7 : daysToMonday);
        return nextMonday - now;
    }
}
