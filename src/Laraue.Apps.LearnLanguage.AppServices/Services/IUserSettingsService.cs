using Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;
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
    /// Show the choices of how others see the user on the leaderboard, previewing their current names.
    /// </summary>
    Task HandleLeaderboardNameSettingsViewAsync(
        ReplyData replyData,
        TelegramProfile? profile,
        CancellationToken ct = default);

    /// <summary>
    /// Set what of the user's names others see on the leaderboard and show the settings.
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