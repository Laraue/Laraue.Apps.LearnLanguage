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
}

/// <param name="Achievement">The achievement.</param>
/// <param name="Level">The level granted, 1 for bronze or a single-level achievement.</param>
/// <param name="Holders">How many players have this level, including the user.</param>
/// <param name="Players">How many users have finished a quiz.</param>
public record EarnedAchievement(Achievement Achievement, byte Level, int Holders, int Players);
