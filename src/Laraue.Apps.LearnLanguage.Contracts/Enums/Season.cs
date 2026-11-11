namespace Laraue.Apps.LearnLanguage.Contracts.Enums;

/// <summary>
/// A leaderboard period whose top players get trophies when it ends. Stored by value: don't renumber.
/// </summary>
public enum Season : byte
{
    /// <summary>Monday 00:00 UTC to the next Monday.</summary>
    Week = 1,

    /// <summary>The 1st 00:00 UTC to the next 1st.</summary>
    Month = 2,
}
