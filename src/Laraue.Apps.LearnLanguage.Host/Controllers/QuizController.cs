using Laraue.Apps.LearnLanguage.AppServices;
using Laraue.Apps.LearnLanguage.AppServices.Services.Quiz;
using Laraue.Telegram.NET.Abstractions.Request;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Routing.Attributes;

namespace Laraue.Apps.LearnLanguage.Host.Controllers;

public class QuizController(IQuizService service) : TelegramController
{
    [TelegramCallbackRoute(TelegramRoutes.CurrentQuiz)]
    public Task OpenQuizWindowAsync(
        RequestContext context,
        [FromQuery] QuizRequest request,
        CancellationToken ct)
    {
        return service.OpenQuizWindowAsync(ReplyData.FromRequest(context), request, ct);
    }
    
    [TelegramCallbackRoute(TelegramRoutes.TopicSelection)]
    public Task OpenSelectTopicWindowAsync(
        RequestContext context,
        [FromQuery] SelectTopicRequest request,
        CancellationToken ct)
    {
        return service.OpenSelectTopicWindowAsync(ReplyData.FromRequest(context), request, ct);
    }
    
    [TelegramCallbackRoute(TelegramRoutes.TopicSelection, RouteMethod.Post)]
    public Task SelectQuizTopicAsync(
        RequestContext context,
        [FromQuery] ChangeTopicRequest request,
        CancellationToken ct)
    {
        return service.ChangeTopicAsync(ReplyData.FromRequest(context), request, ct);
    }
    
    [TelegramCallbackRoute(TelegramRoutes.CefrLevelSelection)]
    public Task OpenSelectCefrLevelSelectionWindowAsync(
        RequestContext context,
        [FromQuery] SelectCefrLevelRequest request,
        CancellationToken ct)
    {
        return service.OpenSelectCefrLevelWindowAsync(ReplyData.FromRequest(context), request, ct);
    }
    
    [TelegramCallbackRoute(TelegramRoutes.CefrLevelSelection, RouteMethod.Post)]
    public Task SelectCefrLevelAsync(
        RequestContext context,
        [FromQuery] ChangeCefrLevelRequest request,
        CancellationToken ct)
    {
        return service.ChangeCefrLevelAsync(ReplyData.FromRequest(context), request, ct);
    }

    [TelegramCallbackRoute(TelegramRoutes.StartQuiz, RouteMethod.Post)]
    public Task StartQuizAsync(
        RequestContext context,
        [FromQuery] StartQuizRequest request,
        CancellationToken ct)
    {
        return service.StartNewQuizAsync(ReplyData.FromRequest(context), request, ct);
    }
    
    [TelegramCallbackRoute(TelegramRoutes.ReviewQuiz, RouteMethod.Post)]
    public Task StartReviewQuizAsync(
        RequestContext context,
        [FromQuery] StartQuizRequest request,
        CancellationToken ct)
    {
        return service.StartReviewQuizAsync(ReplyData.FromRequest(context), request, ct);
    }

    [TelegramCallbackRoute(TelegramRoutes.SelectQuizAnswer, RouteMethod.Post)]
    public Task SelectQuizAnswerAsync(
        RequestContext context,
        [FromQuery] SelectQuizAnswerRequest request,
        CancellationToken ct)
    {
        return service.SelectQuizAnswerAsync(ReplyData.FromRequest(context), request, ct);
    }
    
    [TelegramCallbackRoute(TelegramRoutes.FinishQuiz, RouteMethod.Post)]
    public Task FinishQuizAsync(
        RequestContext context,
        CancellationToken ct)
    {
        return service.FinishQuizAsync(ReplyData.FromRequest(context), ct);
    }
}