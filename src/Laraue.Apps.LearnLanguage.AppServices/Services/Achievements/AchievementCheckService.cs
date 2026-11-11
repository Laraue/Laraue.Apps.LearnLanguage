using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Achievements;

/// <summary>
/// Runs <see cref="IAchievementChecker"/> every few seconds, so a message about new achievements comes right
/// after the quiz results. A plain timer rather than a Laraue.Core job: at this interval the job runner would
/// save its state and log on every run.
/// </summary>
public sealed class AchievementCheckService(IServiceScopeFactory scopeFactory, ILogger<AchievementCheckService> logger)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider
                    .GetRequiredService<IAchievementChecker>()
                    .CheckRequestedAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The next tick tries again.
                logger.LogWarning(ex, "Checking achievements failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
