using TelegramUser = Telegram.Bot.Types.User;

namespace Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;

/// <summary>
/// The names of a user from their Telegram account.
/// </summary>
public record TelegramProfile(string? Username, string? FirstName, string? LastName)
{
    public static TelegramProfile? From(TelegramUser? user) =>
        user is null ? null : new TelegramProfile(user.Username, user.FirstName, user.LastName);
}
