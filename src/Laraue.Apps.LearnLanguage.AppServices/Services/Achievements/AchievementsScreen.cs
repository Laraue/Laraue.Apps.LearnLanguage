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

        // Achievements with levels first, then the single ones as a checklist.
        foreach (var item in overview.Items.Where(i => AchievementCatalog.All[i.Achievement].Thresholds.Length > 1))
        {
            var definition = AchievementCatalog.All[item.Achievement];
            var levels = definition.Thresholds.Length;
            var header = $"{definition.Emoji} <b>{AchievementCatalog.Name(item.Achievement)}</b>";
            tmb.AppendRow();

            if (item.Level == levels)
            {
                tmb.AppendRow($"{header} · {AchievementCatalog.Level(item.Achievement, levels)} · {Rarity(overview, item, item.Level)}");
                continue;
            }

            tmb.AppendRow(item.Level > 0 ? $"{header} · {AchievementCatalog.Level(item.Achievement, item.Level)}" : header);
            var next = item.Level + 1;
            var progress = item.Achievement == Achievement.LevelsCompleted
                ? AchievementCatalog.Requirement(item.Achievement, next)
                : $"{Number(item.Value)} / {AchievementCatalog.Requirement(item.Achievement, next)}";
            tmb.AppendRow(string.Format(
                AchievementTexts.Next,
                $"{AchievementCatalog.Level(item.Achievement, next)} · {progress} · {Rarity(overview, item, next)}"));
        }

        tmb.AppendRow();
        foreach (var item in overview.Items.Where(i => AchievementCatalog.All[i.Achievement].Thresholds.Length == 1))
        {
            var definition = AchievementCatalog.All[item.Achievement];
            var name = $"{definition.Emoji} <b>{AchievementCatalog.Name(item.Achievement)}</b>";
            tmb.AppendRow(item.Level > 0
                ? $"✅ {name} · {Rarity(overview, item, 1)}"
                : $"🔒 {name} · {AchievementCatalog.Requirement(item.Achievement, 1)} · {Rarity(overview, item, 1)}");
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

    private static string Rarity(AchievementsOverview overview, AchievementProgress item, int level) =>
        AchievementCatalog.Rarity(
            overview.Holders.GetValueOrDefault((item.Achievement, (byte)level)),
            overview.Players,
            held: item.Level >= level);

    private static string Number(int value) => value.ToString("N0", CultureInfo.CurrentUICulture);
}
