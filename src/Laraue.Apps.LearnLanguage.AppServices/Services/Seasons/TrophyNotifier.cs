using System.Globalization;
using Laraue.Apps.LearnLanguage.AppServices.Resources;
using Laraue.Apps.LearnLanguage.Contracts.Enums;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;
using Laraue.Telegram.NET.Core.Extensions;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Utils;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using AchievementTexts = Laraue.Apps.LearnLanguage.AppServices.Resources.Achievements;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Seasons;

public sealed class TrophyNotifier(
    DatabaseContext context,
    ITelegramBotClient client,
    IBotUsernameProvider botUsernameProvider)
    : ITrophyNotifier
{
    /// <inheritdoc />
    public async Task NotifyAsync(UserTrophy trophy, CancellationToken ct = default)
    {
        var user = await context.Users
            .Where(u => u.Id == trophy.UserId && !u.AchievementMessagesOff && u.BotBlockedAt == null)
            .Select(u => new { u.TelegramId, u.TelegramLanguageCode })
            .FirstOrDefaultAsync(ct);
        if (user is null)
            return;

        // Sent by a job, outside of the user's request.
        using var _ = InterfaceCulture.Use(user.TelegramLanguageCode);

        var title = TrophyTitle(trophy);
        var tmb = new TelegramMessageBuilder().AppendRow($"<b>{title}</b>");

        var firstRow = new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData(Buttons.Achievement_Ok, TelegramRoutes.DropMessage),
        };
        if (await botUsernameProvider.GetUsernameAsync(ct) is { } botUsername)
        {
            var text = string.Format(AchievementTexts.Trophy_ShareText, title, botUsername);
            var botLink = $"https://t.me/{botUsername}?start=source-trophy";
            firstRow.Add(InlineKeyboardButton.WithUrl(
                Buttons.Achievement_Share,
                $"https://t.me/share/url?url={Uri.EscapeDataString(botLink)}&text={Uri.EscapeDataString(text)}"));
        }

        tmb.AddInlineKeyboardButtons(firstRow);
        tmb.AddInlineKeyboardButtons([
            new CallbackRoutePath(TelegramRoutes.AchievementMessagesOff, RouteMethod.Post)
                .ToInlineKeyboardButton(Buttons.Achievement_DontNotify),
        ]);

        await client.SendTextMessageAsync(user.TelegramId, tmb, parseMode: ParseMode.Html, cancellationToken: ct);
    }

    /// <summary>
    /// "🏆 🥇 1st place · week 46".
    /// </summary>
    public static string TrophyTitle(UserTrophy trophy)
    {
        var place = AchievementTexts.ResourceManager.GetString($"Trophy_Place_{trophy.Place}", AchievementTexts.Culture);
        return $"🏆 {place} · {SeasonName(trophy.Season, trophy.SeasonStart)}";
    }

    private static string SeasonName(Season season, DateTime start)
    {
        if (season == Season.Week)
            return string.Format(AchievementTexts.Trophy_Week, ISOWeek.GetWeekOfYear(start));

        var month = CultureInfo.CurrentUICulture.DateTimeFormat.GetMonthName(start.Month);
        return $"{char.ToUpper(month[0], CultureInfo.CurrentUICulture)}{month[1..]} {start.Year}";
    }
}
