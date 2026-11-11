using Laraue.Apps.LearnLanguage.Contracts.Enums;
using Laraue.Telegram.NET.Abstractions.Request;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Contracts;

/// <summary>
/// The leaderboard for this week, or for this month, of a league.
/// </summary>
public record LeaderboardRequest
{
    [FromQuery(ParameterNames.Month)]
    public bool Month { get; init; }

    /// <summary>
    /// The <see cref="Contracts.Enums.League"/> to show; the viewer's own one when not passed.
    /// </summary>
    [FromQuery(ParameterNames.League)]
    public int League { get; init; } = -1;
}
