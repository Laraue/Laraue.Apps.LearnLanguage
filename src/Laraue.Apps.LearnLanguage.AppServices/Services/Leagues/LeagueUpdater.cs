using Laraue.Apps.LearnLanguage.Contracts.Enums;
using Laraue.Apps.LearnLanguage.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Leagues;

public sealed class LeagueUpdater(DatabaseContext context) : ILeagueUpdater
{
    private const int BatchSize = 500;

    /// <inheritdoc />
    public async Task<int> UpdateAsync(CancellationToken ct = default)
    {
        // Counted in the database; only the ids of users who move up come back.
        var userIds = await context.LearnedTranslations
            .Where(t => t.LearnedAt != null && t.User.League == League.Beginner)
            .GroupBy(t => t.UserId)
            .Where(g => g.Select(t => t.WordId).Distinct().Count() >= LeagueRules.IntermediateMinWords)
            .Select(g => g.Key)
            .ToListAsync(ct);

        foreach (var batch in userIds.Chunk(BatchSize))
        {
            await context.Users
                .Where(u => batch.Contains(u.Id))
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.League, League.Intermediate), ct);
        }

        return userIds.Count;
    }
}
