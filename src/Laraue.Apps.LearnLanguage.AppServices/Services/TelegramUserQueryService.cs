using Laraue.Apps.Identity.Internal.Contracts;
using Laraue.Apps.LearnLanguage.AppServices.Identity;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Telegram.NET.Authentication.Services;
using LinqToDB.EntityFrameworkCore;
using User = Laraue.Apps.LearnLanguage.DataAccess.Entities.User;

namespace Laraue.Apps.LearnLanguage.AppServices.Services;

public class TelegramUserQueryService(DatabaseContext context, UserIdentityService.UserIdentityServiceClient identityClient)
    : ITelegramUserQueryService<Guid>
{
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
        var globalUserId = await identityClient.GetGlobalUserIdAsync(
            telegramData.Id,
            telegramData.Username,
            telegramData.FirstName,
            telegramData.LastName,
            telegramData.LanguageCode,
            cancellationToken);

        var user = new User
        {
            GlobalUserId = globalUserId,
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
}