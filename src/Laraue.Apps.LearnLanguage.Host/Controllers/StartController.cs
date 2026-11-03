using Laraue.Apps.LearnLanguage.AppServices;
using Laraue.Apps.LearnLanguage.AppServices.Services;
using Laraue.Apps.LearnLanguage.AppServices.Services.Quiz;
using Laraue.Telegram.NET.Abstractions.Request;
using Laraue.Telegram.NET.Core.Extensions;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Routing.Attributes;

namespace Laraue.Apps.LearnLanguage.Host.Controllers;

public class StartController(IMenuService service) : TelegramController
{
    [TelegramMessageRoute(TelegramRoutes.Start)]
    public Task StartAsync(RequestContext request, CancellationToken cancellationToken)
    {
        return service.StartAsync(
            request.Update.Message!.Text!,
            request.UserId,
            request.Update.GetUserId(),
            cancellationToken);
    }
    
    [TelegramCallbackRoute(TelegramRoutes.StartFirstQuiz, RouteMethod.Post)]
    public Task StartFirstQuizAsync(
        RequestContext context,
        [FromQuery] StartQuizRequest request,
        CancellationToken cancellationToken)
    {
        return service.StartFirstQuizAsync(
            request,
            ReplyData.FromRequest(context),
            cancellationToken);
    }
}