namespace Laraue.Apps.LearnLanguage.DataAccess.Entities;

/// <summary>
/// A place in the top 3 of the leaderboard for a season. Won again every season.
/// </summary>
public class UserTrophy
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public long SeasonId { get; set; }
    public Season? Season { get; set; }

    /// <summary>
    /// 1, 2 or 3; users with equal points share a place.
    /// </summary>
    public byte Place { get; set; }

    public DateTime AwardedAt { get; set; }
}
