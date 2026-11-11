using Laraue.Apps.LearnLanguage.Contracts.Enums;

namespace Laraue.Apps.LearnLanguage.DataAccess.Entities;

/// <summary>
/// A week or a month of the leaderboard. When it ends, its top players get trophies and it's closed.
/// </summary>
public class Season : BaseEntity
{
    public SeasonType Type { get; set; }

    /// <summary>
    /// The first moment of the season (UTC): a Monday or the 1st, 00:00.
    /// </summary>
    public DateTime StartsAt { get; set; }

    /// <summary>
    /// The moment after the season (UTC): the next season's start.
    /// </summary>
    public DateTime EndsAt { get; set; }

    /// <summary>
    /// When its trophies were awarded; null while it isn't closed. A season recorded when the bot started
    /// awarding trophies is closed without awarding them.
    /// </summary>
    public DateTime? ClosedAt { get; set; }

    public ICollection<UserTrophy> Trophies { get; set; } = null!;
}
