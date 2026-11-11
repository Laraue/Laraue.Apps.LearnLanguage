using Laraue.Apps.LearnLanguage.Contracts.Enums;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Seasons;

public static class Seasons
{
    /// <summary>
    /// The start of the season that contains <paramref name="at"/> (UTC): its Monday or its 1st, 00:00.
    /// </summary>
    public static DateTime StartOf(Season season, DateTime at)
    {
        var day = at.Date;
        return season == Season.Week
            ? day.AddDays(-((7 + (int)day.DayOfWeek - (int)DayOfWeek.Monday) % 7))
            : new DateTime(day.Year, day.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    public static DateTime End(Season season, DateTime start) =>
        season == Season.Week ? start.AddDays(7) : start.AddMonths(1);

    /// <summary>
    /// The start of the last season that has ended by <paramref name="at"/>.
    /// </summary>
    public static DateTime LastEndedStart(Season season, DateTime at)
    {
        var current = StartOf(season, at);
        return season == Season.Week ? current.AddDays(-7) : current.AddMonths(-1);
    }
}
