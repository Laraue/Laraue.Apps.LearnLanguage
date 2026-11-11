using Telegram.Bot.Types;
using Laraue.Apps.LearnLanguage.AppServices.Services.Contracts;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Achievements;

/// <summary>
/// The achievements screens: the list and the choice of a title for the leaderboard.
/// </summary>
public interface IAchievementsScreen
{
    /// <summary>
    /// Every achievement with the user's level, the next level and their progress to it, and how rare it is.
    /// </summary>
    Task SendListAsync(ReplyData replyData, CancellationToken ct = default);

    /// <summary>
    /// The same list as a new message, sent by the achievements job after a check the list asked for.
    /// </summary>
    Task SendListMessageAsync(Guid userId, ChatId chatId, CancellationToken ct = default);

    /// <summary>
    /// The earned level names the user can show next to their name on the leaderboard.
    /// </summary>
    Task SendTitlesAsync(ReplyData replyData, CancellationToken ct = default);

    /// <summary>
    /// Sets the title and shows the list.
    /// </summary>
    Task SetTitleAsync(ReplyData replyData, SetAchievementTitleRequest request, CancellationToken ct = default);
}
