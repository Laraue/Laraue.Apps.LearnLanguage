using Laraue.Apps.Identity.Internal.Contracts;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.LearnLanguage.AppServices.Identity;

/// <summary>
/// Gives every user without a global id the one Identity returns for their Telegram account. Safe to
/// run repeatedly and to restart: it only touches users whose id is still empty.
/// </summary>
public class GlobalUserIdBackfill(
    DatabaseContext context,
    UserIdentityService.UserIdentityServiceClient identityClient,
    IOptions<IdentityOptions> options,
    ILogger<GlobalUserIdBackfill> logger)
{
    /// <returns>How many users got an id.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var batchSize = options.Value.BackfillBatchSize;
        var throttle = options.Value.BackfillThrottle;

        // The column is still nullable while this runs (see the AddGlobalUserId and
        // MakeGlobalUserIdRequired migrations), a state the EF model (Guid, required) can't
        // express in a LINQ filter - hence raw SQL.
        var withoutId = context.Users
            .FromSqlRaw("SELECT * FROM users WHERE global_user_id IS NULL");

        var total = await withoutId.CountAsync(cancellationToken);
        logger.LogInformation("Global user id backfill started, {Count} users to process", total);

        var processed = 0;
        while (true)
        {
            var batch = await withoutId
                .OrderBy(u => u.CreatedAt)
                .ThenBy(u => u.Id)
                .Take(batchSize)
                // Not the whole entity: the nullable column can't be materialized into a Guid yet.
                .Select(u => new User
                {
                    Id = u.Id,
                    TelegramId = u.TelegramId,
                    TelegramUserName = u.TelegramUserName,
                    TelegramFirstName = u.TelegramFirstName,
                    TelegramLastName = u.TelegramLastName,
                    TelegramLanguageCode = u.TelegramLanguageCode,
                })
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
                break;

            foreach (var user in batch)
            {
                var globalUserId = await identityClient.GetGlobalUserIdAsync(
                    user.TelegramId,
                    user.TelegramUserName,
                    user.TelegramFirstName,
                    user.TelegramLastName,
                    user.TelegramLanguageCode,
                    cancellationToken);

                await context.Users
                    .Where(u => u.Id == user.Id)
                    .ExecuteUpdateAsync(u => u.SetProperty(x => x.GlobalUserId, globalUserId), cancellationToken);

                await Task.Delay(throttle, cancellationToken);
            }

            processed += batch.Count;
            logger.LogInformation("Global user id backfill: {Processed}/{Total} users", processed, total);
        }

        logger.LogInformation("Global user id backfill finished, {Processed} users updated", processed);

        return processed;
    }
}
