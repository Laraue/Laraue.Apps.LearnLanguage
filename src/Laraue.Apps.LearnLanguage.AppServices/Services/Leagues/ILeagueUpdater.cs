namespace Laraue.Apps.LearnLanguage.AppServices.Services.Leagues;

/// <summary>
/// Moves users up to the league their learned words have reached.
/// </summary>
public interface ILeagueUpdater
{
    /// <summary>
    /// Moves Beginner users with at least <see cref="LeagueRules.IntermediateMinWords"/> distinct learned words
    /// up to Intermediate. Nobody moves down: a wrong answer un-learns a word, and a user right at the threshold
    /// would otherwise go back and forth between boards.
    /// </summary>
    /// <returns>The users who moved up.</returns>
    Task<IReadOnlyList<Guid>> UpdateAsync(CancellationToken ct = default);
}
