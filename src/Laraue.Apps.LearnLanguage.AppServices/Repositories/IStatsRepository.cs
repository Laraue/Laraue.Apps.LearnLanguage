using Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;

namespace Laraue.Apps.LearnLanguage.AppServices.Repositories;

public interface IStatsRepository
{
    /// <summary>
    /// Progress in the given language pair, or in the user's default one, or in the pair of their last quiz.
    /// Null when the user hasn't practised any pair yet.
    /// </summary>
    Task<ProgressStats?> GetProgressAsync(Guid userId, long? languageId, DateTime now, CancellationToken ct = default);

    /// <summary>
    /// Words of the pair the user answered wrong most often and hasn't learned yet, most missed first.
    /// </summary>
    Task<MissedWord[]> GetMostMissedWordsAsync(Guid userId, long languageId, int count, CancellationToken ct = default);

    /// <summary>
    /// Users of the viewer's league ranked by points for answers given since <paramref name="since"/>, all
    /// language pairs together: the first <paramref name="count"/> of them and the viewer's own row.
    /// </summary>
    Task<Leaderboard> GetLeaderboardAsync(Guid userId, DateTime since, int count, CancellationToken ct = default);
}
