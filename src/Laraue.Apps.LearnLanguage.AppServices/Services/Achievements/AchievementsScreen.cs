using Telegram.Bot.Types;
using Laraue.Core.DateTime.Services.Abstractions;
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
    IDateTimeProvider dateTimeProvider,
    DatabaseContext context,
    ITelegramBotClient client)
    : IAchievementsScreen
{
    /// <inheritdoc />
    public async Task SendListAsync(ReplyData replyData, CancellationToken ct = default)
    {
        var tmb = await BuildListAsync(replyData.UserId, requestMissing: true, ct);
        await client.EditMessageTextAsync(replyData, tmb, ParseMode.Html, cancellationToken: ct);
    }

    /// <inheritdoc />
    public async Task SendListMessageAsync(Guid userId, ChatId chatId, CancellationToken ct = default)
    {
        var tmb = await BuildListAsync(userId, requestMissing: false, ct);
        await client.SendTextMessageAsync(chatId, tmb, parseMode: ParseMode.Html, cancellationToken: ct);
    }

    /// <param name="requestMissing">Ask the achievements job to grant what's reached but not granted yet and
    /// send the list again after it.</param>
    private async Task<TelegramMessageBuilder> BuildListAsync(Guid userId, bool requestMissing, CancellationToken ct)
    {
        var overview = await achievementService.GetOverviewAsync(userId, ct);
        var held = overview.Items.Sum(i => i.Level);

        var tmb = new TelegramMessageBuilder()
            .AppendRow($"<b>{string.Format(AchievementTexts.ScreenTitle, held, AchievementCatalog.LevelsCount)}</b>");

        // Seasonal trophies: won again and again, so not part of the count.
        var trophies = await context.UserTrophies
            .Where(t => t.UserId == userId)
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
        if (requestMissing && overview.Items.Any(IsReachedButNotGranted))
        {
            var now = dateTimeProvider.UtcNow;
            await context.Users
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(x => x.AchievementsCheckRequestedAt, now)
                    .SetProperty(x => x.AchievementsListRequested, true), ct);
            tmb.AppendRow().AppendRow(AchievementTexts.Pending);
        }

        // Achievements with levels first: the level held and what it means, then the next one.
        foreach (var item in overview.Items.Where(i => AchievementCatalog.All[i.Achievement].Thresholds.Length > 1))
        {
            tmb.AppendRow();
            foreach (var line in LevelsLines(item))
                tmb.AppendRow(line);
        }

        // Then the single ones as a checklist, spaced like the others.
        foreach (var item in overview.Items.Where(i => AchievementCatalog.All[i.Achievement].Thresholds.Length == 1))
        {
            // Only the status as an icon, and what it's for.
            tmb.AppendRow().AppendRow(
                $"{(item.Level > 0 ? "✅" : "🔒")} <b>{AchievementCatalog.Name(item.Achievement)}</b> " +
                $"({AchievementCatalog.Meaning(item.Achievement, 1)})");
        }

        var title = await context.Users
            .Where(u => u.Id == userId)
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
        return tmb;
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
