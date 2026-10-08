using Laraue.Apps.Identity.Internal.Contracts;

namespace Laraue.Apps.LearnLanguage.AppServices.Identity;

public static class UserIdentityServiceClientExtensions
{
    /// <summary>
    /// Resolves (or creates) the global Laraue user id of the Telegram account described by the arguments.
    /// Lets any failure (including <see cref="Grpc.Core.RpcException"/>) propagate.
    /// </summary>
    public static async Task<Guid> GetGlobalUserIdAsync(
        this UserIdentityService.UserIdentityServiceClient client,
        long telegramId,
        string? userName,
        string? firstName,
        string? lastName,
        string? languageCode,
        CancellationToken cancellationToken)
    {
        var request = new CreateUserIfNotExistsRequest { TelegramId = telegramId };

        if (userName is not null) request.TelegramUsername = userName;
        if (firstName is not null) request.TelegramFirstName = firstName;
        if (lastName is not null) request.TelegramLastName = lastName;
        if (languageCode is not null) request.TelegramLanguageCode = languageCode;

        var response = await client.CreateUserIfNotExistsAsync(request, cancellationToken: cancellationToken);

        return Guid.Parse(response.UserId);
    }
}
