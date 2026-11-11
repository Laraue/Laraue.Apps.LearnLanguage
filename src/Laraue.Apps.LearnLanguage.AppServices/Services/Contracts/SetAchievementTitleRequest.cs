using Laraue.Telegram.NET.Abstractions.Request;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Contracts;

public record SetAchievementTitleRequest
{
    /// <summary>
    /// The achievement whose level to show; 0 for no title.
    /// </summary>
    [FromQuery(ParameterNames.Achievement)]
    public int Achievement { get; init; }

    [FromQuery(ParameterNames.Level)]
    public int Level { get; init; }
}
