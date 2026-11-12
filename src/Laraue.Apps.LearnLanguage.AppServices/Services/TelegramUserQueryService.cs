using Laraue.Apps.Identity.Internal.Contracts;
using Laraue.Apps.LearnLanguage.AppServices.Identity;
using Laraue.Apps.LearnLanguage.AppServices.Repositories;
using Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Telegram.NET.Authentication.Services;
using LinqToDB.EntityFrameworkCore;
using User = Laraue.Apps.LearnLanguage.DataAccess.Entities.User;

namespace Laraue.Apps.LearnLanguage.AppServices.Services;

public class TelegramUserQueryService(
    DatabaseContext context,
    UserIdentityService.UserIdentityServiceClient identityClient,
    IUserRepository userRepository,
    RequestContext requestContext)
    : ITelegramUserQueryService<Guid>
{
    /// <summary>
    /// Called by the library once in a while for each active user, to keep their names fresh.
    /// </summary>
    public Task UpdateAsync(Guid userId, TelegramData telegramData, CancellationToken cancellationToken)
    {
        return userRepository.UpdateProfileAsync(
            userId,
            new TelegramProfile(telegramData.Username, telegramData.FirstName, telegramData.LastName),
            cancellationToken);
    }

    public async Task<TelegramUserId<Guid>?> FindUserIdAsync(long telegramId, CancellationToken cancellationToken)
    {
        var id = await context.Users
            .Where(u => u.TelegramId == telegramId)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsyncEF(cancellationToken);

        return id is { } userId ? new TelegramUserId<Guid>(userId) : null;
    }

    public async Task<Guid> CreateAsync(TelegramData telegramData, CancellationToken cancellationToken)
    {
        // Resolve the global identity before touching our own DB - if Laraue.Apps.Identity is
        // unreachable, registration fails instead of creating a user without one.
        // Called on the user's first update; for a link like t.me/<bot>?start=source-share that's the /start
        // message, and its source label is where the user came from. No update (a direct call) = no source.
        var globalUser = await identityClient.GetGlobalUserAsync(
            telegramData.Id,
            telegramData.Username,
            telegramData.FirstName,
            telegramData.LastName,
            telegramData.LanguageCode,
            UtmLabels.ParseSource(requestContext.Update?.Message?.Text),
            cancellationToken);

        var user = new User
        {
            GlobalUserId = globalUser.Id,
            LanguageToLearnId = await FindLanguageToLearnIdAsync(telegramData.LanguageCode, cancellationToken),
            TelegramId = telegramData.Id,
            TelegramUserName = telegramData.Username,
            TelegramLanguageCode = telegramData.LanguageCode,
            TelegramFirstName = telegramData.FirstName,
            TelegramLastName = telegramData.LastName,
            CreatedAt = DateTime.UtcNow,
        };

        context.Users.Add(user);

        await context.SaveChangesAsync(cancellationToken);

        return user.Id;
    }

    /// <summary>
    /// The default language pair for a new user: English → the language of their Telegram app, when we have
    /// translations to it. English speakers and unknown languages choose the pair themselves.
    /// </summary>
    private async Task<long?> FindLanguageToLearnIdAsync(string? telegramLanguageCode, CancellationToken ct)
    {
        // An IETF tag like "pt-br" or "zh-hans"; only the language part matters here.
        var code = telegramLanguageCode?.Split('-')[0].ToLowerInvariant();
        if (string.IsNullOrEmpty(code) || code == "en")
        {
            return null;
        }

        return await context.Translations
            .Where(t => t.Language.Name == code)
            .Select(t => (long?)t.LanguageId)
            .FirstOrDefaultAsyncEF(ct);
    }
}
