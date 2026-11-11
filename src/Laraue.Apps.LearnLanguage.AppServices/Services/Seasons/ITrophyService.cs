using Laraue.Apps.LearnLanguage.Contracts.Enums;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Seasons;

/// <summary>
/// Awards trophies to the top 3 of the leaderboard when a season ends.
/// </summary>
public interface ITrophyService
{
    /// <summary>
    /// Awards the season that started at <paramref name="seasonStart"/> and tells the winners. A season needs
    /// <see cref="TrophyService.MinPlayers"/> players for trophies; awarding a season again adds nothing.
    /// </summary>
    /// <returns>How many trophies were awarded.</returns>
    Task<int> AwardAsync(Season season, DateTime seasonStart, CancellationToken ct = default);
}
