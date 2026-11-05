namespace Laraue.Apps.LearnLanguage.AppServices.Services.Quiz;

public interface IQuizService
{
    Task OpenQuizWindowAsync(
        ReplyData replyData,
        QuizRequest request,
        CancellationToken ct = default);

    Task OpenSelectTopicWindowAsync(
        ReplyData replyData,
        SelectTopicRequest request,
        CancellationToken ct = default);

    Task ChangeTopicAsync(
        ReplyData replyData,
        ChangeTopicRequest request,
        CancellationToken ct = default);
    
    Task OpenSelectCefrLevelWindowAsync(
        ReplyData replyData,
        SelectCefrLevelRequest request,
        CancellationToken ct = default);

    Task ChangeCefrLevelAsync(
        ReplyData replyData,
        ChangeCefrLevelRequest request,
        CancellationToken ct = default);

    Task StartNewQuizAsync(
        ReplyData replyData,
        StartQuizRequest startQuizRequest,
        CancellationToken ct = default);

    /// <summary>
    /// Start the short and easy first quiz of a new user and show its first question. If the user already had a
    /// quiz, starts a regular one.
    /// </summary>
    Task StartFirstQuizAsync(
        ReplyData replyData,
        long languageToLearnId,
        CancellationToken ct = default);

    /// <summary>
    /// Start a quiz built from the words the user answered wrong most often and hasn't learned yet, or a regular
    /// quiz when there are none.
    /// </summary>
    Task StartReviewQuizAsync(
        ReplyData replyData,
        StartQuizRequest request,
        CancellationToken ct = default);

    Task SelectQuizAnswerAsync(
        ReplyData replyData,
        SelectQuizAnswerRequest request,
        CancellationToken ct = default);

    Task FinishQuizAsync(
        ReplyData replyData,
        CancellationToken ct = default);
}