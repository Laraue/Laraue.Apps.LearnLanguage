using System.Globalization;
using Laraue.Apps.LearnLanguage.AppServices.Extensions;
using Laraue.Apps.LearnLanguage.AppServices.Resources;
using Laraue.Apps.LearnLanguage.AppServices.Services.Contracts;
using Laraue.Apps.LearnLanguage.Contracts.Enums;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Telegram.NET.Core.Extensions;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Utils;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using AchievementTexts = Laraue.Apps.LearnLanguage.AppServices.Resources.Achievements;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Achievements;

public sealed class AchievementsScreen(
    IAchievementService achievementService,
    IAchievementChecker achievementChecker,
    DatabaseContext context,
    ITelegramBotClient client)
    : IAchievementsScreen
{
    /// <inheritdoc />
    public async Task SendListAsync(ReplyData replyData, CancellationToken ct = default)
    {
        var overview = await achievementService.GetOverviewAsync(replyData.UserId, ct);
        var held = overview.Items.Sum(i => i.Level);

        var tmb = new TelegramMessageBuilder()
            .AppendRow($"<b>{string.Format(AchievementTexts.ScreenTitle, held, AchievementCatalog.LevelsCount)}</b>");

        // Seasonal trophies: won again and again, so not part of the count.
        var trophies = await context.UserTrophies
            .Where(t => t.UserId == replyData.UserId)
            .GroupBy(t => t.Place)
            .Select(g => new { Place = g.Key, Count = g.Count() })
            .OrderBy(x => x.Place)
            .ToListAsync(ct);
        if (trophies.Count > 0)
        {
            string[] medals = ["🥇", "🥈", "🥉"];
            tmb.AppendRow().AppendRow(string.Format(
                AchievementTexts.Trophies,
                string.Join(" · ", trophies.Select(t => $"{medals[t.Place - 1]} ×{t.Count}"))));
        }

        // Reached but not granted yet (e.g. progress made before achievements existed): the achievements job
        // grants them in a few seconds.
        if (overview.Items.Any(IsReachedButNotGranted))
        {
            await achievementChecker.RequestCheckAsync(replyData.UserId, ct);
            var messagesOff = await context.Users
                .Where(u => u.Id == replyData.UserId)
                .Select(u => u.AchievementMessagesOff)
                .FirstAsync(ct);
            tmb.AppendRow().AppendRow(messagesOff ? AchievementTexts.PendingRefresh : AchievementTexts.PendingMessage);
        }

        // Achievements with levels first, one line each: medals held and progress to the next level.
        tmb.AppendRow();
        foreach (var item in overview.Items.Where(i => AchievementCatalog.All[i.Achievement].Thresholds.Length > 1))
            tmb.AppendRow(LevelsLine(item));

        // Then the single ones as a checklist.
        tmb.AppendRow();
        foreach (var item in overview.Items.Where(i => AchievementCatalog.All[i.Achievement].Thresholds.Length == 1))
        {
            var definition = AchievementCatalog.All[item.Achievement];
            var name = $"{definition.Emoji} {AchievementCatalog.Name(item.Achievement)}";
            tmb.AppendRow(item.Level > 0
                ? $"✅ {name}"
                : $"🔒 {name} · {AchievementCatalog.Requirement(item.Achievement, 1)}");
        }

        var title = await context.Users
            .Where(u => u.Id == replyData.UserId)
            .Select(u => new { u.TitleAchievement, u.TitleLevel })
            .FirstAsync(ct);
        tmb.AddInlineKeyboardButtons([
            InlineKeyboardButton.WithCallbackData(
                title.TitleAchievement is { } a && title.TitleLevel is { } l
                    ? string.Format(Buttons.Achievements_Title, AchievementCatalog.ShortTitle(a, l))
                    : Buttons.Achievements_ChooseTitle,
                TelegramRoutes.AchievementTitles)
        ]);
        tmb.AddInlineKeyboardButtons([
            InlineKeyboardButton.WithCallbackData(Buttons.Stat, TelegramRoutes.Stat)
        ]);
        tmb.AddMainMenuButton();

        await client.EditMessageTextAsync(replyData, tmb, ParseMode.Html, cancellationToken: ct);
    }

    /// <inheritdoc />
    public async Task SendTitlesAsync(ReplyData replyData, CancellationToken ct = default)
    {
        var earned = await context.UserAchievements
            .Where(a => a.UserId == replyData.UserId)
            .OrderBy(a => a.AchievedAt)
            .Select(a => new { a.Achievement, a.Level })
            .ToListAsync(ct);
        var current = await context.Users
            .Where(u => u.Id == replyData.UserId)
            .Select(u => new { u.TitleAchievement, u.TitleLevel })
            .FirstAsync(ct);

        var tmb = new TelegramMessageBuilder()
            .AppendRow(earned.Count == 0 ? AchievementTexts.NoTitles : AchievementTexts.ChooseTitle);

        var path = new CallbackRoutePath(TelegramRoutes.AchievementTitles, RouteMethod.Post).Freeze();
        foreach (var title in earned)
        {
            var selected = current.TitleAchievement == title.Achievement && current.TitleLevel == title.Level;
            var text = AchievementCatalog.ShortTitle(title.Achievement, title.Level);
            tmb.AddInlineKeyboardButtons([
                path.WithQueryParameter(ParameterNames.Achievement, (int)title.Achievement)
                    .WithQueryParameter(ParameterNames.Level, (int)title.Level)
                    .ToInlineKeyboardButton(selected ? $"✅ {text}" : text)
            ]);
        }

        if (earned.Count > 0)
        {
            var noTitle = Buttons.Achievements_NoTitle;
            tmb.AddInlineKeyboardButtons([
                path.WithQueryParameter(ParameterNames.Achievement, 0)
                    .ToInlineKeyboardButton(current.TitleAchievement is null ? $"✅ {noTitle}" : noTitle)
            ]);
        }

        tmb.AddBackMenuButton(TelegramRoutes.Achievements);
        await client.EditMessageTextAsync(replyData, tmb, ParseMode.Html, cancellationToken: ct);
    }

    /// <inheritdoc />
    public async Task SetTitleAsync(
        ReplyData replyData,
        SetAchievementTitleRequest request,
        CancellationToken ct = default)
    {
        var achievement = Enum.IsDefined((Achievement)request.Achievement) ? (Achievement?)request.Achievement : null;
        await achievementService.SetTitleAsync(
            replyData.UserId,
            achievement,
            achievement is null ? null : (byte)request.Level,
            ct);
        await SendListAsync(replyData, ct);
    }

    /// <summary>
    /// "📚 Words learned: 🥉🥈🥇 · 2,289 / 3,000", or the next level for levels completed.
    /// </summary>
    private static string LevelsLine(AchievementProgress item)
    {
        var definition = AchievementCatalog.All[item.Achievement];
        var levels = definition.Thresholds.Length;
        var medals = string.Concat(Enumerable.Range(1, item.Level).Select(l => AchievementCatalog.Medal(l)));

        string? progress = item.Achievement == Achievement.LevelsCompleted
            ? item.Level < levels
                ? string.Format(AchievementTexts.NextLevel, AchievementCatalog.Requirement(item.Achievement, item.Level + 1))
                : null
            : item.Level < levels
                ? $"{Number(item.Value)} / {AchievementCatalog.Requirement(item.Achievement, item.Level + 1)}"
                : Number(item.Value);

        var details = string.Join(" · ", new[] { medals, progress }.Where(x => !string.IsNullOrEmpty(x)));
        return $"{definition.Emoji} {AchievementCatalog.Name(item.Achievement)}: {details}";
    }

    private static bool IsReachedButNotGranted(AchievementProgress item) =>
        AchievementCatalog.All[item.Achievement].LevelsReached(item.Value).Any(level => level > item.Level);

    private static string Number(int value) => value.ToString("N0", CultureInfo.CurrentUICulture);
}
