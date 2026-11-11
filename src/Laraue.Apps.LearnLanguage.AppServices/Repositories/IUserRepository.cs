using Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;

namespace Laraue.Apps.LearnLanguage.AppServices.Repositories;

public interface IUserRepository
{
    Task<UserSettings> GetSettingsAsync(Guid userId, CancellationToken ct = default);
    
    Task SetLanguageCodeAsync(Guid userId, string code, CancellationToken ct = default);

    Task SetReminderTimeAsync(Guid userId, TimeOnly? reminderTime, CancellationToken ct = default);

    /// <summary>
    /// Whether other users see the user's first name on the leaderboard.
    /// </summary>
    Task SetShowNameOnLeaderboardAsync(Guid userId, bool show, CancellationToken ct = default);
    
    Task UpdateLanguageSettingsAsync(
        Guid userId,
        SelectedTranslation selectedTranslation,
        CancellationToken ct = default);
}