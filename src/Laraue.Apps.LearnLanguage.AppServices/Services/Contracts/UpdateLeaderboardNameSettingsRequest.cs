using Laraue.Apps.LearnLanguage.Contracts.Enums;
using Laraue.Telegram.NET.Abstractions.Request;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Contracts;

public record UpdateLeaderboardNameSettingsRequest
{
    /// <summary>
    /// What of the user's names others see on the leaderboard, a <see cref="LeaderboardNameMode"/>.
    /// </summary>
    [FromQuery(ParameterNames.LeaderboardName)]
    public int Mode { get; init; }
}
