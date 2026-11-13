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
        if (earned.Count == 0)
            return;

        var user = await context.Users
            .Where(u => u.Id == userId && !u.AchievementMessagesOff && u.BotBlockedAt == null)
            .Select(u => new { u.TelegramLanguageCode })
            .FirstOrDefaultAsync(ct);
        if (user is null)
            return;

        // Sent by the achievements job, outside of the user's request.
        using var _ = InterfaceCulture.Use(user.TelegramLanguageCode);

        // A heading once, then each achievement: its level and what it was earned for, with how rare it is.
        var tmb = new TelegramMessageBuilder()
            .AppendRow($"<b>{(earned.Count == 1 ? AchievementTexts.NewOne : AchievementTexts.NewMany)}</b>");
        foreach (var achievement in earned)
        {
            tmb.AppendRow()
                .AppendRow($"<b>{AchievementCatalog.Heading(achievement.Achievement, achievement.Level)}</b>")
                .AppendRow($"{AchievementCatalog.Description(achievement.Achievement, achievement.Level)} · {Rarity(achievement)}");
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
    /// A Telegram share link with just the text: the level and what it was earned for, and the bot's @username,
    /// which Telegram makes clickable, so no separate link to the bot (MIN-52).
    /// </summary>
    private static string GetShareUrl(EarnedAchievement achievement, string botUsername)
    {
        var text = string.Format(
            AchievementTexts.ShareText,
            $"{AchievementCatalog.All[achievement.Achievement].Emoji} " +
            AchievementCatalog.LevelWithMeaning(achievement.Achievement, achievement.Level),
            botUsername,
            Rarity(achievement));
        return ShareLinks.CreateTextOnly(text);
    }

    private static string Rarity(EarnedAchievement achievement) =>
        AchievementCatalog.Rarity(achievement.Holders, achievement.Players, held: true);
}
