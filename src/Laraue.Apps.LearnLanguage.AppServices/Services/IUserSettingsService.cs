using Laraue.Apps.LearnLanguage.AppServices.Services.Contracts;

namespace Laraue.Apps.LearnLanguage.AppServices.Services;

public interface IUserSettingsService
{
    Task HandleSettingsViewAsync(ReplyData replyData, CancellationToken ct = default);
    
    Task HandleInterfaceLanguageSettingsViewAsync(
        ReplyData replyData,
        UpdateInterfaceLanguageSettingsRequest request,
        CancellationToken ct = default);
    
    Task HandleLearnLanguageSettingsViewAsync(ReplyData replyData, CancellationToken ct = default);
    
    /// <summary>
    /// Turn the daily reminder on or off and show the settings.
    /// </summary>
    Task UpdateRemindersSettingsAsync(
        ReplyData replyData,
        UpdateRemindersSettingsRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Show or mask the user's name on the leaderboard and show the settings.
    /// </summary>
    Task UpdateLeaderboardNameSettingsAsync(
        ReplyData replyData,
        UpdateLeaderboardNameSettingsRequest request,
        CancellationToken ct = default);

    Task UpdateLearnLanguageSettingsAsync(
        ReplyData replyData,
        UpdateLearnLanguageSettingsRequest request,
        CancellationToken ct = default);
}