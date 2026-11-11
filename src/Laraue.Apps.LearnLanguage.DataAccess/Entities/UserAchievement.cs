using Laraue.Apps.LearnLanguage.Contracts.Enums;

namespace Laraue.Apps.LearnLanguage.DataAccess.Entities;

/// <summary>
/// A level of an achievement a user has earned; every level reached is a row. Earned once, kept forever.
/// </summary>
public class UserAchievement
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Achievement Achievement { get; set; }

    /// <summary>
    /// 1 for bronze (or a single-level achievement), 2 for silver, and so on.
    /// </summary>
    public byte Level { get; set; }

    /// <summary>
    /// When the user earned it (UTC).
    /// </summary>
    public DateTime AchievedAt { get; set; }
}
