using Laraue.Apps.LearnLanguage.Contracts.Enums;
using Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;

namespace Laraue.Apps.LearnLanguage.AppServices.Repositories;

public interface IUserRepository
{
    Task<UserSettings> GetSettingsAsync(Guid userId, CancellationToken ct = default);
    
    Task SetLanguageCodeAsync(Guid userId, string code, CancellationToken ct = default);

    Task SetReminderTimeAsync(Guid userId, TimeOnly? reminderTime, CancellationToken ct = default);

    /// <summary>
    /// What of the user's names others see on the leaderboard.
    /// </summary>
    Task SetLeaderboardNameAsync(Guid userId, LeaderboardNameMode mode, CancellationToken ct = default);

    /// <summary>
    /// Whether the user gets messages about new achievements. They earn them either way.
    /// </summary>
    Task SetAchievementMessagesOffAsync(Guid userId, bool off, CancellationToken ct = default);

    /// <summary>
    /// Save the user's current Telegram names, when they changed. The interface language isn't touched: the
    /// user may have chosen another one in the settings.
    /// </summary>
    Task UpdateProfileAsync(Guid userId, TelegramProfile profile, CancellationToken ct = default);
    
    Task UpdateLanguageSettingsAsync(
        Guid userId,
        SelectedTranslation selectedTranslation,
        CancellationToken ct = default);
}