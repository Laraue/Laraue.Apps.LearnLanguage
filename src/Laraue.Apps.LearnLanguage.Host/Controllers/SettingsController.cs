using Laraue.Apps.LearnLanguage.AppServices;
using Laraue.Apps.LearnLanguage.AppServices.Services;
using Laraue.Apps.LearnLanguage.AppServices.Services.Contracts;
using Laraue.Telegram.NET.Abstractions.Request;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Routing.Attributes;

namespace Laraue.Apps.LearnLanguage.Host.Controllers;

public class SettingsController(IUserSettingsService service) : TelegramController
{
    [TelegramCallbackRoute(TelegramRoutes.Settings)]
    public Task HandleSettingsRequestAsync(RequestContext request, CancellationToken ct)
    {
        return service.HandleSettingsViewAsync(ReplyData.FromRequest(request), ct);
    }
    
    [TelegramCallbackRoute(TelegramRoutes.InterfaceLanguageSettings)]
    public Task HandleInterfaceLanguageSettingsRequestAsync(
        RequestContext context,
        [FromQuery] UpdateInterfaceLanguageSettingsRequest request,
        CancellationToken ct)
    {
        return service.HandleInterfaceLanguageSettingsViewAsync(ReplyData.FromRequest(context), request, ct);
    }
    
    [TelegramCallbackRoute(TelegramRoutes.LearnLanguageSettings)]
    public Task ViewLearnLanguageSettingsAsync(
        RequestContext context,
        CancellationToken ct)
    {
        return service.HandleLearnLanguageSettingsViewAsync(ReplyData.FromRequest(context), ct);
    }
    
    [TelegramCallbackRoute(TelegramRoutes.RemindersSettings, RouteMethod.Post)]
    public Task UpdateRemindersSettingsAsync(
        RequestContext context,
        [FromQuery] UpdateRemindersSettingsRequest request,
        CancellationToken ct)
    {
        return service.UpdateRemindersSettingsAsync(ReplyData.FromRequest(context), request, ct);
    }

    [TelegramCallbackRoute(TelegramRoutes.LeaderboardNameSettings, RouteMethod.Post)]
    public Task UpdateLeaderboardNameSettingsAsync(
        RequestContext context,
        [FromQuery] UpdateLeaderboardNameSettingsRequest request,
        CancellationToken ct)
    {
        return service.UpdateLeaderboardNameSettingsAsync(ReplyData.FromRequest(context), request, ct);
    }

    [TelegramCallbackRoute(TelegramRoutes.LearnLanguageSettings, RouteMethod.Post)]
    public async Task UpdateLearnLanguageSettingsAsync(
        RequestContext context,
        [FromQuery] UpdateLearnLanguageSettingsRequest request,
        CancellationToken ct)
    {
        await service.UpdateLearnLanguageSettingsAsync(ReplyData.FromRequest(context), request, ct);

        await HandleSettingsRequestAsync(context, ct);
    }
}