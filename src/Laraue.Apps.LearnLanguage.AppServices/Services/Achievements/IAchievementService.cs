using Laraue.Apps.LearnLanguage.Contracts.Enums;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Achievements;

/// <summary>
/// Grants achievements and tells how rare they are.
/// </summary>
public interface IAchievementService
{
    /// <summary>
    /// Grants the levels of achievements the user has reached and hasn't had yet, from their current progress.
    /// A user who already meets several (e.g. learned words before achievements existed) gets them all at once.
    /// Nothing is granted before the user has finished a quiz with answers.
    /// </summary>
    /// <returns>The highest level just granted of each achievement, with how many players have it now.</returns>
    Task<IReadOnlyList<EarnedAchievement>> GrantAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// The user's levels and progress in every achievement, and how many players have each level.
    /// </summary>
    Task<AchievementsOverview> GetOverviewAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// How many achievement levels the user has.
    /// </summary>
    Task<int> CountHeldAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Shows the name of an earned level next to the user's name on the leaderboard, or no title for null.
    /// </summary>
    /// <returns>False when the user hasn't earned that level.</returns>
    Task<bool> SetTitleAsync(Guid userId, Achievement? achievement, byte? level, CancellationToken ct = default);
}

/// <param name="Items">Each achievement, in the catalog's order.</param>
/// <param name="Holders">How many players have each level.</param>
/// <param name="Players">How many users have finished a quiz.</param>
public record AchievementsOverview(
    IReadOnlyList<AchievementProgress> Items,
    IReadOnlyDictionary<AchievementLevel, int> Holders,
    int Players);

/// <param name="Achievement">The achievement.</param>
/// <param name="Level">The user's highest level, 0 for none.</param>
/// <param name="Value">The user's progress, compared to the achievement's thresholds.</param>
public record AchievementProgress(Achievement Achievement, byte Level, int Value);

/// <param name="Achievement">The achievement.</param>
/// <param name="Level">The level granted, 1 for bronze or a single-level achievement.</param>
/// <param name="Holders">How many players have this level, including the user.</param>
/// <param name="Players">How many users have finished a quiz.</param>
public record EarnedAchievement(Achievement Achievement, byte Level, int Holders, int Players);
