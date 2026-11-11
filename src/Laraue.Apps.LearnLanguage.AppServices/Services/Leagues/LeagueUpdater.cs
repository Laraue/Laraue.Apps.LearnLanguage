using Laraue.Apps.LearnLanguage.Contracts.Enums;
using Laraue.Apps.LearnLanguage.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Leagues;

/// <summary>
/// Recounts the words each user has learned and moves users up to the Intermediate league when they reach
/// <see cref="LeagueRules.IntermediateMinWords"/>. Nobody moves down: a wrong answer un-learns a word, and a user
/// right at the threshold would otherwise go back and forth between boards.
/// </summary>
public sealed class LeagueUpdater(DatabaseContext context)
{
    /// <returns>How many users changed.</returns>
    public Task<int> UpdateAsync(CancellationToken ct = default)
    {
        const byte intermediate = (byte)League.Intermediate;

        // One statement; rows are written only when the count or the league changes.
        return context.Database.ExecuteSqlInterpolatedAsync($"""
            WITH counts AS (
                SELECT u.id,
                       (SELECT count(DISTINCT t.word_id)
                        FROM learned_translations t
                        WHERE t.user_id = u.id AND t.learned_at IS NOT NULL)::int AS words
                FROM users u
            )
            UPDATE users u
            SET learned_words = c.words,
                league = CASE WHEN c.words >= {LeagueRules.IntermediateMinWords} THEN {intermediate} ELSE u.league END
            FROM counts c
            WHERE c.id = u.id
              AND (u.learned_words <> c.words
                   OR (c.words >= {LeagueRules.IntermediateMinWords} AND u.league <> {intermediate}))
            """, ct);
    }
}
