using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Leagues;

/// <summary>
/// Runs <see cref="LeagueUpdater"/> on start and then every few minutes, so a user moves up within one interval
/// of learning their 1,000th word.
/// </summary>
public sealed class LeagueService(IServiceScopeFactory scopeFactory, ILogger<LeagueService> logger)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var changed = await scope.ServiceProvider
                    .GetRequiredService<LeagueUpdater>()
                    .UpdateAsync(stoppingToken);
                logger.LogInformation("Leagues updated, {Count} users changed", changed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The next tick tries again.
                logger.LogWarning(ex, "Updating leagues failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
