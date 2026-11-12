using Laraue.Apps.Identity.Internal.Contracts;

namespace Laraue.Apps.LearnLanguage.AppServices.Identity;

public static class UserIdentityServiceClientExtensions
{
    /// <summary>
    /// Resolves (or creates) the global Laraue user of the Telegram account described by the arguments.
    /// <paramref name="source"/> is where the user came from (the /start link's source label); Identity
    /// normalizes it and records it only on the user's first call from this service.
    /// Lets any failure (including <see cref="Grpc.Core.RpcException"/>) propagate.
    /// </summary>
    public static async Task<GlobalUser> GetGlobalUserAsync(
        this UserIdentityService.UserIdentityServiceClient client,
        long telegramId,
        string? userName,
        string? firstName,
        string? lastName,
        string? languageCode,
        string? source,
        CancellationToken cancellationToken)
    {
        var request = new CreateUserIfNotExistsRequest { TelegramId = telegramId };

        if (userName is not null) request.TelegramUsername = userName;
        if (firstName is not null) request.TelegramFirstName = firstName;
        if (lastName is not null) request.TelegramLastName = lastName;
        if (languageCode is not null) request.TelegramLanguageCode = languageCode;
        if (source is not null) request.Source = source;

        var response = await client.CreateUserIfNotExistsAsync(request, cancellationToken: cancellationToken);

        return new GlobalUser(
            Guid.Parse(response.UserId),
            response.HasSource ? response.Source : null);
    }
}

/// <summary>
/// A user in Laraue.Apps.Identity: their global id, and the sign-up source Identity stored for this service
/// (normalized; null when none was ever sent).
/// </summary>
public sealed record GlobalUser(Guid Id, string? SignUpSource);
