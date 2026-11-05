using Microsoft.Extensions.Logging;
using Telegram.Bot;

namespace Laraue.Apps.LearnLanguage.AppServices.Services;

public interface IBotUsernameProvider
{
    /// <summary>
    /// The bot's Telegram username without "@", or null when it isn't known (Telegram unreachable so far).
    /// </summary>
    Task<string?> GetUsernameAsync(CancellationToken ct = default);
}

/// <summary>
/// Asks Telegram for the bot's username once (getMe) and keeps it; a failed call is retried next time.
/// </summary>
public sealed class BotUsernameProvider(ITelegramBotClient client, ILogger<BotUsernameProvider> logger)
    : IBotUsernameProvider
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _username;

    public async Task<string?> GetUsernameAsync(CancellationToken ct = default)
    {
        if (_username is not null)
        {
            return _username;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_username is null)
            {
                var me = await client.GetMe(ct);
                _username = me.Username;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Getting the bot username failed");
        }
        finally
        {
            _lock.Release();
        }

        return _username;
    }
}
