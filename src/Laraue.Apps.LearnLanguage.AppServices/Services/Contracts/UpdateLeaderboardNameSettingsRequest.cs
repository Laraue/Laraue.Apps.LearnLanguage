using Laraue.Telegram.NET.Abstractions.Request;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Contracts;

public record UpdateLeaderboardNameSettingsRequest
{
    /// <summary>
    /// Show the user's first name on the leaderboard, or mask it.
    /// </summary>
    [FromQuery(ParameterNames.Enable)]
    public bool Show { get; init; }
}
