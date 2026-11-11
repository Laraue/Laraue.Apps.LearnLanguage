using Laraue.Telegram.NET.Abstractions.Request;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Contracts;

public record UpdateAchievementMessagesSettingsRequest
{
    /// <summary>
    /// Send messages about new achievements, or not.
    /// </summary>
    [FromQuery(ParameterNames.Enable)]
    public bool Enabled { get; init; }
}
