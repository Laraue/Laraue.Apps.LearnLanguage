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

        // Earned only: achievements with levels (the level held, then the next one), then the single ones.
        // Locked ones are counted here and listed on their own screen.
        AppendItems(tmb, overview.Items.Where(i => i.Level > 0));
        var locked = overview.Items.Count(i => i.Level == 0);
        if (locked > 0)
            tmb.AppendRow().AppendRow(string.Format(AchievementTexts.LockedCount, locked));

        var title = await context.Users
            .Where(u => u.Id == replyData.UserId)
            .Select(u => new { u.TitleAchievement, u.TitleLevel })
            .FirstAsync(ct);
        if (locked > 0)
        {
            tmb.AddInlineKeyboardButtons([
                InlineKeyboardButton.WithCallbackData(Buttons.Achievements_ShowLocked, TelegramRoutes.AchievementsLocked)
            ]);
        }
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
    public async Task SendLockedAsync(ReplyData replyData, CancellationToken ct = default)
    {
        var overview = await achievementService.GetOverviewAsync(replyData.UserId, ct);
        var tmb = new TelegramMessageBuilder().AppendRow($"<b>{AchievementTexts.LockedTitle}</b>");
        AppendItems(tmb, overview.Items.Where(i => i.Level == 0));
        tmb.AddBackMenuButton(TelegramRoutes.Achievements);

        await client.EditMessageTextAsync(replyData, tmb, ParseMode.Html, cancellationToken: ct);
    }

    /// <summary>
    /// Achievements with levels first (the level held and what it means, then the next one), then the single ones
    /// with their status and what they're for; each separated by a blank line.
    /// </summary>
    private static void AppendItems(TelegramMessageBuilder tmb, IEnumerable<AchievementProgress> items)
    {
        var list = items.ToList();
        foreach (var item in list.Where(i => AchievementCatalog.All[i.Achievement].Thresholds.Length > 1))
        {
            tmb.AppendRow();
            foreach (var line in LevelsLines(item))
                tmb.AppendRow(line);
        }

        foreach (var item in list.Where(i => AchievementCatalog.All[i.Achievement].Thresholds.Length == 1))
        {
            tmb.AppendRow().AppendRow(
                $"{(item.Level > 0 ? "✅" : "🔒")} <b>{AchievementCatalog.Name(item.Achievement)}</b> " +
                $"({AchievementCatalog.Meaning(item.Achievement, 1)})");
        }
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
            // What the title means, as on the achievements list.
            var text = AchievementCatalog.LevelWithMeaning(title.Achievement, title.Level);
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
    /// "Words learned: 🥇 Scholar (1,000 words learned)" (🔒 before a first level), then
    /// "Next: 💎 Walking dictionary · 2,289 of 3,000 words" until the last level.
    /// </summary>
    private static IEnumerable<string> LevelsLines(AchievementProgress item)
    {
        var held = item.Level > 0 ? AchievementCatalog.LevelWithMeaning(item.Achievement, item.Level) : "🔒";
        yield return $"<b>{AchievementCatalog.Name(item.Achievement)}</b>: {held}";

        if (item.Level == AchievementCatalog.All[item.Achievement].Thresholds.Length)
            yield break;

        var next = item.Level + 1;
        var progress = string.Format(
            AchievementCatalog.ProgressFormat(item.Achievement, next),
            Number(item.Value),
            AchievementCatalog.Requirement(item.Achievement, next));
        yield return string.Format(AchievementTexts.Next, AchievementCatalog.Level(item.Achievement, next), progress);
    }

    private static bool IsReachedButNotGranted(AchievementProgress item) =>
        AchievementCatalog.All[item.Achievement].LevelsReached(item.Value).Any(level => level > item.Level);

    private static string Number(int value) => value.ToString("N0", CultureInfo.CurrentUICulture);
}
