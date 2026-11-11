using Laraue.Apps.LearnLanguage.AppServices.Repositories;
using Laraue.Apps.LearnLanguage.Contracts.Enums;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Seasons;

public sealed class TrophyService(
    DatabaseContext context,
    IStatsRepository statsRepository,
    ITrophyNotifier trophyNotifier,
    IDateTimeProvider dateTimeProvider,
    ILogger<TrophyService> logger)
    : ITrophyService
{
    /// <summary>
    /// Fewer players and a place means little: the only player would be the champion.
    /// </summary>
    public const int MinPlayers = 3;

    private const int Places = 3;

    /// <inheritdoc />
    public async Task<int> AwardAsync(Season season, DateTime seasonStart, CancellationToken ct = default)
    {
        var seasonEnd = Seasons.End(season, seasonStart);
        var alreadyAwarded = (await context.UserTrophies
                .Where(t => t.Season == season && t.SeasonStart == seasonStart)
                .Select(t => t.UserId)
                .ToListAsync(ct))
            .ToHashSet();

        var now = dateTimeProvider.UtcNow;
        var top = await statsRepository.GetSeasonTopAsync(seasonStart, seasonEnd, Places, ct);
        if (top.PlayersCount < MinPlayers)
            return 0;

        var trophies = top.Placed
            .Where(p => !alreadyAwarded.Contains(p.UserId))
            .Select(p => new UserTrophy
            {
                UserId = p.UserId,
                Season = season,
                SeasonStart = seasonStart,
                Place = (byte)p.Place,
                AwardedAt = now,
            })
            .ToList();

        context.UserTrophies.AddRange(trophies);
        await context.SaveChangesAsync(ct);

        foreach (var trophy in trophies)
        {
            try
            {
                await trophyNotifier.NotifyAsync(trophy, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The trophy is kept; only the message is lost.
                logger.LogWarning(ex, "Telling user {UserId} about a trophy failed", trophy.UserId);
            }
        }

        return trophies.Count;
    }
}
