using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Reminders;

/// <summary>
/// Runs <see cref="ReminderSender"/> every few minutes. A user's reminder goes out at most one interval after
/// its time.
/// </summary>
public sealed class ReminderService(
    IServiceScopeFactory scopeFactory,
    IDateTimeProvider dateTimeProvider,
    ILogger<ReminderService> logger)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider
                    .GetRequiredService<ReminderSender>()
                    .SendDueAsync(dateTimeProvider.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The next tick tries again.
                logger.LogWarning(ex, "Sending reminders failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
