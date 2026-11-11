using Telegram.Bot.Types;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Achievements;

/// <summary>
/// Tells users about their new achievements.
/// </summary>
public interface IAchievementNotifier
{
    /// <summary>
    /// Sends a message about <paramref name="earned"/> with buttons to close it or to turn such messages off.
    /// Nothing is sent when nothing was earned or the user turned the messages off.
    /// </summary>
    Task NotifyAsync(
        Guid userId,
        ChatId chatId,
        IReadOnlyList<EarnedAchievement> earned,
        CancellationToken ct = default);
}
