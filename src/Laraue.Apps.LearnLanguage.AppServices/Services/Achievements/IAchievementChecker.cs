namespace Laraue.Apps.LearnLanguage.AppServices.Services.Achievements;

/// <summary>
/// Checks achievements of users who asked for it by finishing a quiz.
/// </summary>
public interface IAchievementChecker
{
    /// <summary>
    /// Asks to check the user's achievements: the next run of the achievements job does it.
    /// </summary>
    Task RequestCheckAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Grants achievements to the users waiting for a check and tells them. A user's request is cleared only if
    /// no newer quiz moved it meanwhile; a failed check stays requested and is tried again.
    /// </summary>
    /// <returns>How many users were checked.</returns>
    Task<int> CheckRequestedAsync(CancellationToken ct = default);
}
