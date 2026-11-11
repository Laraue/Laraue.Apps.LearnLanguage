using Laraue.Apps.LearnLanguage.AppServices;
using Laraue.Apps.LearnLanguage.AppServices.Services.Achievements;
using Laraue.Apps.LearnLanguage.AppServices.Services.Contracts;
using Laraue.Telegram.NET.Abstractions.Request;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Routing.Attributes;

namespace Laraue.Apps.LearnLanguage.Host.Controllers;

public class AchievementsController(IAchievementsScreen screen) : TelegramController
{
    [TelegramCallbackRoute(TelegramRoutes.Achievements)]
    public Task SendListAsync(RequestContext context, CancellationToken ct)
    {
        return screen.SendListAsync(ReplyData.FromRequest(context), ct);
    }

    [TelegramCallbackRoute(TelegramRoutes.AchievementTitles)]
    public Task SendTitlesAsync(RequestContext context, CancellationToken ct)
    {
        return screen.SendTitlesAsync(ReplyData.FromRequest(context), ct);
    }

    [TelegramCallbackRoute(TelegramRoutes.AchievementTitles, RouteMethod.Post)]
    public Task SetTitleAsync(
        RequestContext context,
        [FromQuery] SetAchievementTitleRequest request,
        CancellationToken ct)
    {
        return screen.SetTitleAsync(ReplyData.FromRequest(context), request, ct);
    }
}
