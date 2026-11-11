using Laraue.Telegram.NET.Core.Extensions;
using Telegram.Bot;
using Laraue.Apps.LearnLanguage.AppServices;
using Laraue.Apps.LearnLanguage.AppServices.Services;
using Laraue.Apps.LearnLanguage.AppServices.Services.Contracts;
using Laraue.Telegram.NET.Abstractions.Request;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Routing.Attributes;

namespace Laraue.Apps.LearnLanguage.Host.Controllers;

public class SettingsController(IUserSettingsService service, ITelegramBotClient client) : TelegramController
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

    [TelegramCallbackRoute(TelegramRoutes.LeaderboardNameSettings)]
    public Task ViewLeaderboardNameSettingsAsync(RequestContext context, CancellationToken ct)
    {
        return service.HandleLeaderboardNameSettingsViewAsync(ReplyData.FromRequest(context), ct);
    }

    [TelegramCallbackRoute(TelegramRoutes.LeaderboardNameSettings, RouteMethod.Post)]
    public Task UpdateLeaderboardNameSettingsAsync(
        RequestContext context,
        [FromQuery] UpdateLeaderboardNameSettingsRequest request,
        CancellationToken ct)
    {
        return service.UpdateLeaderboardNameSettingsAsync(ReplyData.FromRequest(context), request, ct);
    }

    [TelegramCallbackRoute(TelegramRoutes.AchievementMessagesSettings, RouteMethod.Post)]
    public Task UpdateAchievementMessagesSettingsAsync(
        RequestContext context,
        [FromQuery] UpdateAchievementMessagesSettingsRequest request,
        CancellationToken ct)
    {
        return service.UpdateAchievementMessagesSettingsAsync(ReplyData.FromRequest(context), request, ct);
    }

    /// <summary>
    /// "Don't notify me" under an achievement message: turns the messages off and removes this one.
    /// </summary>
    [TelegramCallbackRoute(TelegramRoutes.AchievementMessagesOff, RouteMethod.Post)]
    public async Task TurnOffAchievementMessagesAsync(RequestContext context, CancellationToken ct)
    {
        await service.TurnOffAchievementMessagesAsync(context.UserId, ct);
        await client.DeleteMessage(
            context.Update.GetUserId(),
            context.Update.CallbackQuery!.Message!.MessageId,
            ct);
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