namespace Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;

/// <summary>
/// The names of a user from their Telegram account.
/// </summary>
public record TelegramProfile(string? Username, string? FirstName, string? LastName);
