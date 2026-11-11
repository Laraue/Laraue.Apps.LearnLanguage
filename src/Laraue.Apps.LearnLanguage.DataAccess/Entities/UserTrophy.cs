using Laraue.Apps.LearnLanguage.Contracts.Enums;

namespace Laraue.Apps.LearnLanguage.DataAccess.Entities;

/// <summary>
/// A place in the top 3 of a league for a week or a month. Won again every season.
/// </summary>
public class UserTrophy
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Season Season { get; set; }

    /// <summary>
    /// When the season started (UTC): a Monday for a week, the 1st for a month.
    /// </summary>
    public DateTime SeasonStart { get; set; }

    /// <summary>
    /// The league the user played in.
    /// </summary>
    public League League { get; set; }

    /// <summary>
    /// 1, 2 or 3; users with equal points share a place.
    /// </summary>
    public byte Place { get; set; }

    public DateTime AwardedAt { get; set; }
}
