using Laraue.Apps.LearnLanguage.AppServices.Services.Quiz;

namespace Laraue.Apps.LearnLanguage.AppServices.Services;

/// <summary>
/// Handle main menu commands.
/// </summary>
public interface IMenuService
{
    /// <summary>
    /// Send bot menu to the user.
    /// </summary>
    Task SendMenuAsync(ReplyData replyData, CancellationToken ct = default);
    
    Task SendWordsListsMenuAsync(ReplyData replyData, CancellationToken ct = default);

    /// <summary>
    /// Send start messages to the user.
    /// </summary>
    Task StartAsync(
        string rawCommand,
        Guid userId,
        long telegramId,
        CancellationToken ct = default);

    /// <summary>
    /// Handle the first quiz button of the start message: save the chosen language pair as the default one when
    /// none is set, and show the first question.
    /// </summary>
    Task StartFirstQuizAsync(
        StartQuizRequest request,
        ReplyData replyData,
        CancellationToken ct = default);
}