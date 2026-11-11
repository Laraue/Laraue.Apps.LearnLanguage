using Laraue.Telegram.NET.Abstractions.Request;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Contracts;

/// <summary>
/// The leaderboard for this week, or for this month.
/// </summary>
public record LeaderboardRequest
{
    [FromQuery(ParameterNames.Month)]
    public bool Month { get; init; }
}
