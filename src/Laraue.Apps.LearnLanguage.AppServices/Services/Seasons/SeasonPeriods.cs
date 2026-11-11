using Laraue.Apps.LearnLanguage.Contracts.Enums;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Seasons;

/// <summary>
/// Where weeks and months start and end (UTC).
/// </summary>
public static class SeasonPeriods
{
    /// <summary>
    /// The start of the season that contains <paramref name="at"/>: its Monday or its 1st, 00:00.
    /// </summary>
    public static DateTime StartOf(SeasonType type, DateTime at)
    {
        var day = at.Date;
        return type == SeasonType.Week
            ? day.AddDays(-((7 + (int)day.DayOfWeek - (int)DayOfWeek.Monday) % 7))
            : new DateTime(day.Year, day.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    public static DateTime EndOf(SeasonType type, DateTime start) =>
        type == SeasonType.Week ? start.AddDays(7) : start.AddMonths(1);

    /// <summary>
    /// The start of the last season that has ended by <paramref name="at"/>.
    /// </summary>
    public static DateTime LastEndedStart(SeasonType type, DateTime at)
    {
        var current = StartOf(type, at);
        return type == SeasonType.Week ? current.AddDays(-7) : current.AddMonths(-1);
    }
}
