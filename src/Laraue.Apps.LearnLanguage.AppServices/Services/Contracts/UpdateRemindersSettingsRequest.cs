using Laraue.Telegram.NET.Abstractions.Request;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Contracts;

public record UpdateRemindersSettingsRequest
{
    /// <summary>
    /// Turn the daily reminder on (at the current time of day) or off.
    /// </summary>
    [FromQuery(ParameterNames.Enable)]
    public bool Enabled { get; init; }
}
