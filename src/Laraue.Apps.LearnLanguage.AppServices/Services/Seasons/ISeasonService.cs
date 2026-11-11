namespace Laraue.Apps.LearnLanguage.AppServices.Services.Seasons;

/// <summary>
/// Closes leaderboard seasons and awards their trophies.
/// </summary>
public interface ISeasonService
{
    /// <summary>
    /// Records the last ended week and month and closes every season that isn't closed yet: its top 3 get
    /// trophies and a message. A season needs <see cref="SeasonService.MinPlayers"/> players for trophies.
    /// When no season of a type is recorded yet (the first run), the ended one is closed without trophies,
    /// so past seasons aren't awarded.
    /// </summary>
    /// <returns>How many trophies were awarded.</returns>
    Task<int> CloseEndedSeasonsAsync(CancellationToken ct = default);
}
