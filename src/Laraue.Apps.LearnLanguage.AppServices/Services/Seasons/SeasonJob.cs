using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Seasons;

/// <summary>
/// Runs daily at 00:00 UTC and closes the seasons that have ended: on Monday the week, on the 1st the month.
/// Which seasons are closed is kept in the seasons table, so a missed run catches up and a repeated one does
/// nothing.
/// </summary>
public sealed class SeasonJob(
    ISeasonService seasonService,
    IDateTimeProvider dateTimeProvider,
    ILogger<SeasonJob> logger)
    : BaseJob
{
    public const string Name = "Seasons";

    private static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);

    public override async Task<TimeSpan> ExecuteAsync(
        JobState<EmptyJobData> jobState,
        CancellationToken stoppingToken = default)
    {
        var now = dateTimeProvider.UtcNow;
        try
        {
            await seasonService.CloseEndedSeasonsAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Closing seasons failed, retrying in {RetryInterval}", RetryInterval);
            return RetryInterval;
        }

        return TimeToNextDay(now);
    }

    /// <summary>
    /// Time from <paramref name="now"/> to the next 00:00 UTC.
    /// </summary>
    public static TimeSpan TimeToNextDay(DateTime now) => now.Date.AddDays(1) - now;
}
