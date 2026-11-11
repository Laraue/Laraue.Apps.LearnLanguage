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

    /// <summary>
    /// Days in a row with a quiz started or a question answered (UTC), up to today. A streak that ended yesterday
    /// still counts, so it doesn't look broken before the user has practised today.
    /// </summary>
    Task<int> GetStreakAsync(Guid userId, DateOnly today, CancellationToken ct = default);

    /// <summary>
    /// Players of a league who placed in the top <paramref name="places"/> for answers given from
    /// <paramref name="since"/> to <paramref name="until"/>, ranked as on the leaderboard: equal points and
    /// right answers share a place. Only players with points above zero place.
    /// </summary>
    Task<SeasonTop> GetSeasonTopAsync(
        League league,
        DateTime since,
        DateTime until,
        int places,
        CancellationToken ct = default);
}

/// <param name="PlayersCount">How many users of the league answered in the season.</param>
/// <param name="Placed">The players who placed, best first.</param>
public record SeasonTop(int PlayersCount, IReadOnlyList<SeasonPlace> Placed);

/// <param name="UserId">The player.</param>
/// <param name="Place">1-based place.</param>
public record SeasonPlace(Guid UserId, int Place);
