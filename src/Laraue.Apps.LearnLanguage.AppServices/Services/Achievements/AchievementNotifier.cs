using Laraue.Apps.LearnLanguage.AppServices.Services;
using System.Globalization;
using Laraue.Apps.LearnLanguage.AppServices.Resources;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Telegram.NET.Core.Extensions;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Utils;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using AchievementTexts = Laraue.Apps.LearnLanguage.AppServices.Resources.Achievements;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Achievements;

public sealed class AchievementNotifier(
    DatabaseContext context,
    ITelegramBotClient client,
    IBotUsernameProvider botUsernameProvider)
    : IAchievementNotifier
{
    /// <inheritdoc />
    public async Task NotifyAsync(
        Guid userId,
        ChatId chatId,
        IReadOnlyList<EarnedAchievement> earned,
        CancellationToken ct = default)
    {
        if (earned.Count == 0
            || await context.Users.AnyAsync(u => u.Id == userId && (u.AchievementMessagesOff || u.BotBlockedAt != null), ct))
        {
            return;
        }

        var tmb = new TelegramMessageBuilder();
        foreach (var achievement in earned)
        {
            if (tmb.Text.Length > 0)
                tmb.AppendRow();
            tmb.AppendRow($"<b>{string.Format(
                    AchievementTexts.New,
                    AchievementCatalog.Title(achievement.Achievement, achievement.Level))}</b>")
                .AppendRow(Rarity(achievement));
        }

        var firstRow = new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData(Buttons.Achievement_Ok, TelegramRoutes.DropMessage),
        };
        if (await botUsernameProvider.GetUsernameAsync(ct) is { } botUsername)
        {
            // Several at once: share the rarest.
            var rarest = earned.MinBy(a => (double)a.Holders / Math.Max(a.Players, a.Holders))!;
            firstRow.Add(InlineKeyboardButton.WithUrl(Buttons.Achievement_Share, GetShareUrl(rarest, botUsername)));
        }

        tmb.AddInlineKeyboardButtons(firstRow);
        tmb.AddInlineKeyboardButtons([
            new CallbackRoutePath(TelegramRoutes.AchievementMessagesOff, RouteMethod.Post)
                .ToInlineKeyboardButton(Buttons.Achievement_DontNotify),
        ]);

        await client.SendTextMessageAsync(chatId, tmb, parseMode: ParseMode.Html, cancellationToken: ct);
    }

    /// <summary>
    /// A Telegram share link with the achievement and a link to the bot. The link's start parameter is the UTM
    /// label source=achievement, so users who join through it show up in the funnel.
    /// </summary>
    private static string GetShareUrl(EarnedAchievement achievement, string botUsername)
    {
        var text = string.Format(
            AchievementTexts.ShareText,
            AchievementCatalog.Title(achievement.Achievement, achievement.Level),
            botUsername,
            Rarity(achievement));
        var botLink = $"https://t.me/{botUsername}?start=source-achievement";
        return $"https://t.me/share/url?url={Uri.EscapeDataString(botLink)}&text={Uri.EscapeDataString(text)}";
    }

    private static string Rarity(EarnedAchievement achievement)
    {
        if (achievement.Holders <= 1)
            return AchievementTexts.OnlyYou;

        var percent = achievement.Holders * 100.0 / Math.Max(achievement.Players, achievement.Holders);
        return percent < 1
            ? AchievementTexts.RarityUnderOnePercent
            : string.Format(AchievementTexts.Rarity, Math.Round(percent).ToString(CultureInfo.CurrentUICulture));
    }
}
