using Laraue.Apps.Identity.Internal.Contracts;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;

namespace Laraue.Apps.LearnLanguage.AppServices.Identity;

public static class UserIdentityServiceClientExtensions
{
    /// <summary>
    /// Resolves (or creates) the global Laraue user id of the Telegram account of <paramref name="user"/>.
    /// Lets any failure (including <see cref="Grpc.Core.RpcException"/>) propagate.
    /// </summary>
    public static async Task<Guid> GetGlobalUserIdAsync(
        this UserIdentityService.UserIdentityServiceClient client,
        User user,
        CancellationToken cancellationToken)
    {
        var request = new CreateUserIfNotExistsRequest { TelegramId = user.TelegramId };

        if (user.TelegramUserName is { } userName) request.TelegramUsername = userName;
        if (user.TelegramFirstName is { } firstName) request.TelegramFirstName = firstName;
        if (user.TelegramLastName is { } lastName) request.TelegramLastName = lastName;
        if (user.TelegramLanguageCode is { } languageCode) request.TelegramLanguageCode = languageCode;

        var response = await client.CreateUserIfNotExistsAsync(request, cancellationToken: cancellationToken);

        return Guid.Parse(response.UserId);
    }
}
