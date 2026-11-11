using Laraue.Apps.LearnLanguage.AppServices.Repositories;
using Laraue.Apps.LearnLanguage.Contracts.Enums;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Seasons;

public sealed class SeasonService(
    DatabaseContext context,
    IStatsRepository statsRepository,
    ITrophyNotifier trophyNotifier,
    IDateTimeProvider dateTimeProvider,
    ILogger<SeasonService> logger)
    : ISeasonService
{
    /// <summary>
    /// Fewer players and a place means little: the only player would be the champion.
    /// </summary>
    public const int MinPlayers = 3;

    private const int Places = 3;

    /// <inheritdoc />
    public async Task<int> CloseEndedSeasonsAsync(CancellationToken ct = default)
    {
        var now = dateTimeProvider.UtcNow;
        foreach (var type in Enum.GetValues<SeasonType>())
            await RecordLastEndedAsync(type, now, ct);

        var awarded = 0;
        var toClose = await context.Seasons
            .Where(s => s.ClosedAt == null && s.EndsAt <= now)
            .OrderBy(s => s.StartsAt)
            .ToListAsync(ct);
        foreach (var season in toClose)
            awarded += await CloseAsync(season, now, ct);

        return awarded;
    }

    private async Task RecordLastEndedAsync(SeasonType type, DateTime now, CancellationToken ct)
    {
        var startsAt = SeasonPeriods.LastEndedStart(type, now);
        if (await context.Seasons.AnyAsync(s => s.Type == type && s.StartsAt == startsAt, ct))
            return;

        var isFirst = !await context.Seasons.AnyAsync(s => s.Type == type, ct);
        context.Seasons.Add(new Season
        {
            Type = type,
            StartsAt = startsAt,
            EndsAt = SeasonPeriods.EndOf(type, startsAt),
            // Trophies start with the next season, not with the ones played before them.
            ClosedAt = isFirst ? now : null,
        });
        await context.SaveChangesAsync(ct);
    }

    private async Task<int> CloseAsync(Season season, DateTime now, CancellationToken ct)
    {
        var top = await statsRepository.GetSeasonTopAsync(season.StartsAt, season.EndsAt, Places, ct);
        var trophies = top.PlayersCount < MinPlayers
            ? []
            : top.Placed
                .Select(p => new UserTrophy
                {
                    UserId = p.UserId,
                    SeasonId = season.Id,
                    Place = (byte)p.Place,
                    AwardedAt = now,
                })
                .ToList();

        // Trophies and closing in one save: a season is either awarded or not.
        context.UserTrophies.AddRange(trophies);
        season.ClosedAt = now;
        await context.SaveChangesAsync(ct);
        logger.LogInformation(
            "{Type} from {StartsAt:yyyy-MM-dd} closed, {Count} trophies",
            season.Type,
            season.StartsAt,
            trophies.Count);

        foreach (var trophy in trophies)
        {
            try
            {
                await trophyNotifier.NotifyAsync(trophy, season, ct);
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
