using Telegram.Bot.Types;

using Laraue.Apps.LearnLanguage.AppServices.Services.Contracts;

namespace Laraue.Apps.LearnLanguage.AppServices.Services;

public interface IStatsService
{
    /// <summary>
    /// Send learning stats for the user.
    /// </summary>
    Task SendStatsAsync(ReplyData replyData, StatsRequest request, CancellationToken ct = default);
    
    /// <summary>
    /// Send admin stats to the user.
    /// </summary>
    Task SendAdminStatsAsync(ChatId telegramId, CancellationToken ct = default);
}