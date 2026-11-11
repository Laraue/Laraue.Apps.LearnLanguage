using Laraue.Apps.LearnLanguage.Contracts.Enums;
namespace Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;

public record UserSettings(
    string? InterfaceLanguageCode,
    long? LanguageToLearnId,
    string? LanguageToLearnCode,
    string? LanguageToLearnTitle,
    TimeOnly? ReminderTime,
    LeaderboardNameMode LeaderboardName,
    TelegramProfile Profile,
    bool AchievementMessagesOff);