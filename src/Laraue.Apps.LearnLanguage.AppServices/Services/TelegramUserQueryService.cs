using Laraue.Apps.Identity.Internal.Contracts;
using Laraue.Apps.LearnLanguage.AppServices.Identity;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Telegram.NET.Authentication.Services;
using LinqToDB.EntityFrameworkCore;
using User = Laraue.Apps.LearnLanguage.DataAccess.Entities.User;

namespace Laraue.Apps.LearnLanguage.AppServices.Services;

public class TelegramUserQueryService(DatabaseContext context, UserIdentityService.UserIdentityServiceClient identityClient)
    : ITelegramUserQueryService<User, Guid>
{
    public Task<User?> FindAsync(long telegramId, CancellationToken cancellationToken)
    {
        return context.Users
            .Where(u => u.TelegramId == telegramId)
            .FirstOrDefaultAsyncEF(cancellationToken);
    }

    public async Task<Guid> CreateAsync(User user, CancellationToken cancellationToken)
    {
        // Resolve the global identity before touching our own DB - if Laraue.Apps.Identity is
        // unreachable, registration fails instead of creating a user without one.
        user.GlobalUserId = await identityClient.GetGlobalUserIdAsync(user, cancellationToken);

        context.Users.Add(user);
        
        await context.SaveChangesAsync(cancellationToken);
        
        return user.Id;
    }
}