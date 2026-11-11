namespace Laraue.Apps.LearnLanguage.Contracts.Enums;

/// <summary>
/// What of the user's Telegram profile others see on the leaderboard.
/// </summary>
public enum LeaderboardNameMode : byte
{
    /// <summary>
    /// Nothing, the name is masked.
    /// </summary>
    Hidden = 0,

    FirstName = 1,

    LastName = 2,

    /// <summary>
    /// First name, then last name.
    /// </summary>
    FullName = 3,

    /// <summary>
    /// The @username, a link to the user's Telegram profile.
    /// </summary>
    Username = 4,
}
