namespace Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;

public record UserSettings(
    string? InterfaceLanguageCode,
    long? LanguageToLearnId,
    string? LanguageToLearnCode,
    string? LanguageToLearnTitle,
    TimeOnly? ReminderTime,
    bool ShowNameOnLeaderboard);