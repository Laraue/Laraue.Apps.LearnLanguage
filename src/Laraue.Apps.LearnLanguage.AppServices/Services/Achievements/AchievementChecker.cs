using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Achievements;

public sealed class AchievementChecker(
    DatabaseContext context,
    IAchievementService achievementService,
    IAchievementNotifier achievementNotifier,
    IDateTimeProvider dateTimeProvider,
    ILogger<AchievementChecker> logger)
    : IAchievementChecker
{
    private const int BatchSize = 100;

    /// <inheritdoc />
    public Task RequestCheckAsync(Guid userId, CancellationToken ct = default)
    {
        var now = dateTimeProvider.UtcNow;
        return context.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.AchievementsCheckRequestedAt, now), ct);
    }

    /// <inheritdoc />
    public async Task<int> CheckRequestedAsync(CancellationToken ct = default)
    {
        var requested = await context.Users
            .Where(u => u.AchievementsCheckRequestedAt != null)
            .OrderBy(u => u.AchievementsCheckRequestedAt)
            .Select(u => new { u.Id, u.TelegramId, RequestedAt = u.AchievementsCheckRequestedAt!.Value })
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var user in requested)
        {
            try
            {
                var earned = await achievementService.GrantAsync(user.Id, ct);
                await achievementNotifier.NotifyAsync(user.Id, user.TelegramId, earned, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The request stays, so the next run tries again. Granted levels are kept, so a failed message
                // isn't sent twice.
                logger.LogWarning(ex, "Checking achievements of user {UserId} failed", user.Id);
                context.ChangeTracker.Clear();
                continue;
            }

            // Cleared only if it's still the request that was checked: a quiz finished meanwhile keeps it.
            await context.Users
                .Where(u => u.Id == user.Id && u.AchievementsCheckRequestedAt == user.RequestedAt)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.AchievementsCheckRequestedAt, (DateTime?)null), ct);
        }

        return requested.Count;
    }
}
