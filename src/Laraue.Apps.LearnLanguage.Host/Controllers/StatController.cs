using Laraue.Apps.LearnLanguage.AppServices;
using Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;
using Laraue.Apps.LearnLanguage.AppServices.Services;
using Laraue.Apps.LearnLanguage.AppServices.Services.Contracts;
using Laraue.Telegram.NET.Abstractions.Request;
using Laraue.Telegram.NET.Core.Extensions;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Routing.Attributes;

namespace Laraue.Apps.LearnLanguage.Host.Controllers;

public class StatController : TelegramController
{
    private readonly IStatsService _statsService;

    public StatController(IStatsService statsService)
    {
        _statsService = statsService;
    }

    [TelegramCallbackRoute(TelegramRoutes.Stat)]
    public Task SendStatsAsync(RequestContext request, [FromQuery] StatsRequest statsRequest, CancellationToken ct)
    {
        return _statsService.SendStatsAsync(ReplyData.FromRequest(request), statsRequest, ct);
    }

    [TelegramCallbackRoute(TelegramRoutes.Leaderboard)]
    public Task SendLeaderboardAsync(
        RequestContext request,
        [FromQuery] LeaderboardRequest leaderboardRequest,
        CancellationToken ct)
    {
        return _statsService.SendLeaderboardAsync(
            ReplyData.FromRequest(request),
            leaderboardRequest,
            TelegramProfile.From(request.Update.GetUser()),
            ct);
    }
}