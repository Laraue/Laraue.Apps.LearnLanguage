using Laraue.Apps.LearnLanguage.Contracts.Enums;
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
    /// Users of a league ranked by points for answers given since <paramref name="since"/>, all language pairs
    /// together: the first <paramref name="count"/> of them and, when it's the viewer's league, their own row.
    /// </summary>
    /// <param name="league">The league to show; the viewer's own one when null.</param>
    Task<Leaderboard> GetLeaderboardAsync(
        Guid userId,
        League? league,
        DateTime since,
        int count,
        CancellationToken ct = default);
}
