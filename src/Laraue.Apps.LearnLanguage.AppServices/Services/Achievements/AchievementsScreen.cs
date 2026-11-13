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
            .AppendRow($"<h3>{string.Format(AchievementTexts.ScreenTitle, held, AchievementCatalog.LevelsCount)}</h3>");

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
            tmb.AppendRow($"<p>{string.Format(
                AchievementTexts.Trophies,
                string.Join(" · ", trophies.Select(t => $"{medals[t.Place - 1]} ×{t.Count}")))}</p>");
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
            tmb.AppendRow($"<p>{(messagesOff ? AchievementTexts.PendingRefresh : AchievementTexts.PendingMessage)}</p>");
        }

        // Earned only: achievements with levels (the level held, then the next one), then the single ones.
        // Locked ones are counted here and listed on their own screen.
        AppendTable(tmb, overview.Items.Where(i => i.Level > 0));
        var locked = overview.Items.Count(i => i.Level == 0);
        if (locked > 0)
            tmb.AppendRow($"<footer>{string.Format(AchievementTexts.LockedCount, locked)}</footer>");

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

        await client.EditRichMessageAsync(replyData, tmb, cancellationToken: ct);
    }

    /// <inheritdoc />
    public async Task SendLockedAsync(ReplyData replyData, CancellationToken ct = default)
    {
        var overview = await achievementService.GetOverviewAsync(replyData.UserId, ct);
        var tmb = new TelegramMessageBuilder().AppendRow($"<h3>{AchievementTexts.LockedTitle}</h3>");
        AppendTable(tmb, overview.Items.Where(i => i.Level == 0));
        tmb.AddBackMenuButton(TelegramRoutes.Achievements);

        await client.EditRichMessageAsync(replyData, tmb, cancellationToken: ct);
    }

    /// <summary>
    /// The achievements as a rich message table (MIN-49): achievements with levels first, then the single ones.
    /// </summary>
    private static void AppendTable(TelegramMessageBuilder tmb, IEnumerable<AchievementProgress> items)
    {
        var list = items.ToList();
        if (list.Count == 0)
            return;

        tmb.AppendRow("<table bordered compact>")
            .AppendRow($"<tr><th>{AchievementTexts.Column_Achievement}</th><th>{AchievementTexts.Column_Level}</th>" +
                       $"<th align=\"right\">{AchievementTexts.Column_Next}</th></tr>")
            .AppendRows(list
                .OrderBy(i => AchievementCatalog.All[i.Achievement].Thresholds.Length == 1)
                .Select(TableRow))
            .AppendRow("</table>");
    }

    /// <summary>
    /// "📚 Words learned | 🥇 Scholar (1,000 words learned) | 💎 2,289 / 3,000": the level held (🔒 and what the
    /// first one takes before it) and the progress to the next level, "–" at the last one. A single-level achievement shows what it's for,
    /// ✅ when earned.
    /// </summary>
    private static string TableRow(AchievementProgress item)
    {
        var definition = AchievementCatalog.All[item.Achievement];
        var name = $"{definition.Emoji} {AchievementCatalog.Name(item.Achievement)}";
        string level, next;
        if (definition.Thresholds.Length == 1)
        {
            level = $"{(item.Level > 0 ? "✅" : "🔒")} {AchievementCatalog.Description(item.Achievement, 1)}";
            next = NoNext;
        }
        else
        {
            // Before a first level, what it takes, as for a single achievement.
            level = item.Level > 0
                ? AchievementCatalog.LevelWithMeaning(item.Achievement, item.Level)
                : $"🔒 {AchievementCatalog.Description(item.Achievement, 1)}";
            next = item.Level == definition.Thresholds.Length ? NoNext : NextLevel(item);
        }

        return $"<tr><td>{name}</td><td>{level}</td><td align=\"right\">{next}</td></tr>";
    }

    private const string NoNext = "–";

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
    /// The next level's medal and the progress to it: "💎 2,289 / 3,000". When the progress isn't a count, the
    /// achievement's own wording: "🥈 learn every A2 word".
    /// </summary>
    private static string NextLevel(AchievementProgress item)
    {
        var next = item.Level + 1;
        var requirement = AchievementCatalog.Requirement(item.Achievement, next);
        var format = AchievementCatalog.ProgressFormat(item.Achievement, next);
        var progress = format.Contains("{0}")
            ? $"{Number(item.Value)} / {requirement}"
            : string.Format(format, Number(item.Value), requirement);
        return $"{AchievementCatalog.Medal(next)} {progress}";
    }

    private static bool IsReachedButNotGranted(AchievementProgress item) =>
        AchievementCatalog.All[item.Achievement].LevelsReached(item.Value).Any(level => level > item.Level);

    private static string Number(int value) => value.ToString("N0", CultureInfo.CurrentUICulture);
}
