using Laraue.Apps.LearnLanguage.DataAccess.Entities;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Seasons;

/// <summary>
/// Tells a user about their trophy, with the same buttons as achievement messages.
/// </summary>
public interface ITrophyNotifier
{
    /// <summary>
    /// Sends a message about <paramref name="trophy"/>, unless the user turned achievement messages off or
    /// blocked the bot.
    /// </summary>
    Task NotifyAsync(UserTrophy trophy, CancellationToken ct = default);
}
