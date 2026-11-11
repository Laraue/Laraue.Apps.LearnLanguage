using System.Globalization;
using Laraue.Apps.LearnLanguage.AppServices.Extensions;
using Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;
using Laraue.Apps.LearnLanguage.AppServices.Services.Contracts;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Apps.LearnLanguage.AppServices.Repositories;
using Laraue.Apps.LearnLanguage.AppServices.Resources;
using Laraue.Telegram.NET.Core.Extensions;
using Laraue.Telegram.NET.Core.Utils;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Laraue.Apps.LearnLanguage.AppServices.Services;

public class StatsService(
    ITelegramBotClient client,
    IStatsRepository statsRepository,
    IAdminRepository adminRepository,
    IDateTimeProvider dateTimeProvider,
    IBotUsernameProvider botUsernameProvider,
    IUserRepository userRepository) : IStatsService
{
    private const int BarLength = 10;
    private const int LeaderboardSize = 10;

    // Fewer players than this and a ranking means little, so the board invites to play instead.
    private const int MinLeaderboardPlayers = 3;

    public async Task SendStatsAsync(ReplyData replyData, StatsRequest request, CancellationToken ct = default)
    {
        var progress = await statsRepository.GetProgressAsync(
            replyData.UserId,
            request.LanguageToLearnId,
            dateTimeProvider.UtcNow,
            ct);

        var tmb = new TelegramMessageBuilder();
        if (progress is null)
        {
            tmb.AppendRow(Stats.NoPractice)
                .AddInlineKeyboardButtons([
                    InlineKeyboardButton.WithCallbackData(QuizMode.ButtonName, TelegramRoutes.CurrentQuiz)
                ])
                .AddMainMenuButton();
            await client.EditMessageTextAsync(replyData, tmb, ParseMode.Html, cancellationToken: ct);
            return;
        }

        tmb.AppendRow($"<b>{string.Format(Stats.Title, progress.LanguageTitle)}</b>")
            .AppendRow();

        if (progress.StreakDays > 0)
            tmb.AppendRow(string.Format(Stats.Streak, progress.StreakDays));
        if (progress.WeekQuizzes > 0 || progress.WeekAnswers > 0)
            tmb.AppendRow(string.Format(
                Stats.Week,
                progress.WeekQuizzes,
                progress.WeekAnswers,
                Percent(progress.WeekCorrectAnswers, progress.WeekAnswers)));

        tmb.AppendRow()
            .AppendRow(string.Format(Stats.Learned, Number(progress.Learned)))
            .AppendRow(string.Format(Stats.InProgress, Number(progress.InProgress)))
            .AppendRow(string.Format(Stats.NotStarted, Number(progress.Total - progress.Learned - progress.InProgress)))
            .AppendRow()
            .AppendRow(Stats.ByLevel);

        // Monospace, so the bars and numbers line up.
        var totalWidth = progress.Levels.Max(l => l.Total.ToString().Length);
        var levelRows = progress.Levels.Select(l =>
        {
            var percent = l.Total == 0 ? 0 : l.Learned * 100.0 / l.Total;
            var filled = (int)Math.Round(percent / 100 * BarLength);
            var bar = new string('▓', filled) + new string('░', BarLength - filled);
            var counts = $"{l.Learned}/{l.Total}".PadLeft(totalWidth * 2 + 1);
            // A decimal below 10%, so the first learned words don't show as 0%.
            var shownPercent = percent is > 0 and < 10
                ? percent.ToString("0.0", CultureInfo.CurrentUICulture)
                : percent.ToString("0", CultureInfo.CurrentUICulture);
            return $"{l.Level} {bar} {shownPercent + "%",5} {counts}";
        });
        tmb.AppendRow($"<pre>{string.Join("\n", levelRows)}</pre>")
            .AppendRow()
            .AppendRow(Stats.PerDay)
            .AppendRow(string.Join(" · ", progress.LastDays.Select(d =>
                $"{CultureInfo.CurrentUICulture.DateTimeFormat.GetShortestDayName(d.Date.DayOfWeek)} {d.Learned}")));

        if (progress.MostMissed.Count > 0)
        {
            tmb.AppendRow()
                .AppendRow(string.Format(
                    Stats.MostMissed,
                    string.Join(" · ", progress.MostMissed.Select(w => $"{w.Word} ✗{w.Misses}"))));
        }

        var pair = new SelectedTranslation(progress.LanguageId);
        var quizButtons = new List<InlineKeyboardButton>();
        if (progress.MostMissed.Count > 0)
        {
            quizButtons.Add(new CallbackRoutePath(TelegramRoutes.ReviewQuiz, RouteMethod.Post)
                .WithTranslationDirection(pair)
                .ToInlineKeyboardButton(Buttons.ReviewMissed));
        }
        quizButtons.Add(new CallbackRoutePath(TelegramRoutes.StartQuiz, RouteMethod.Post)
            .WithTranslationDirection(pair)
            .ToInlineKeyboardButton(QuizMode.ButtonName));
        tmb.AddInlineKeyboardButtons(quizButtons);

        if (await botUsernameProvider.GetUsernameAsync(ct) is { } botUsername)
        {
            tmb.AddInlineKeyboardButtons([
                InlineKeyboardButton.WithUrl(Buttons.ShareProgress, GetShareUrl(progress, botUsername))
            ]);
        }

        // Other pairs the user has practised, to switch the screen to them.
        foreach (var other in progress.PracticedPairs.Where(p => p.LanguageId != progress.LanguageId))
        {
            tmb.AddInlineKeyboardButtons([
                new CallbackRoutePath(TelegramRoutes.Stat)
                    .WithTranslationDirection(new SelectedTranslation(other.LanguageId))
                    .ToInlineKeyboardButton($"📊 English → {other.Title}")
            ]);
        }

        tmb.AddMainMenuButton();
        await client.EditMessageTextAsync(replyData, tmb, ParseMode.Html, cancellationToken: ct);
    }

    public async Task SendLeaderboardAsync(
        ReplyData replyData,
        LeaderboardRequest request,
        TelegramProfile? profile,
        CancellationToken ct = default)
    {
        // The viewer's own row shows their current name.
        if (profile is not null)
            await userRepository.UpdateProfileAsync(replyData.UserId, profile, ct);

        var today = DateOnly.FromDateTime(dateTimeProvider.UtcNow);
        var since = request.Month
            ? new DateOnly(today.Year, today.Month, 1)
            : today.AddDays(-((7 + (int)today.DayOfWeek - (int)DayOfWeek.Monday) % 7));
        var board = await statsRepository.GetLeaderboardAsync(
            replyData.UserId,
            since.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            LeaderboardSize,
            ct);

        var tmb = new TelegramMessageBuilder();
        tmb.AppendRow($"<b>{string.Format(
                Stats.Leaderboard_Title,
                request.Month ? Stats.Leaderboard_Month : Stats.Leaderboard_Week)}</b>")
            .AppendRow(Stats.Leaderboard_Scoring)
            .AppendRow();

        if (board.PlayersCount < MinLeaderboardPlayers)
        {
            tmb.AppendRow(Stats.Leaderboard_FewPlayers);
            if (board.Me is { } me)
                tmb.AppendRow().AppendRow(LeaderboardRow(me, replyData.UserId));
        }
        else
        {
            foreach (var row in board.Top)
                tmb.AppendRow(LeaderboardRow(row, replyData.UserId));
            // Below the shown rows, or tied with the last of them but listed after.
            if (board.Me is { } me && board.Top.All(r => r.UserId != me.UserId))
                tmb.AppendRow("…").AppendRow(LeaderboardRow(me, replyData.UserId));
        }

        if (board.Me is { } my && LeaderboardNames.GetName(my.NameMode, my.Profile) is null)
            tmb.AppendRow().AppendRow(Stats.Leaderboard_NameHidden);

        var periodPath = new CallbackRoutePath(TelegramRoutes.Leaderboard);
        tmb.AddInlineKeyboardButtons([
            periodPath
                .WithQueryParameter(ParameterNames.Month, false)
                .ToInlineKeyboardButton(Selected(Buttons.Leaderboard_Week, !request.Month)),
            periodPath
                .WithQueryParameter(ParameterNames.Month, true)
                .ToInlineKeyboardButton(Selected(Buttons.Leaderboard_Month, request.Month)),
        ]);
        tmb.AddInlineKeyboardButtons([
            InlineKeyboardButton.WithCallbackData(Buttons.Stat, TelegramRoutes.Stat)
        ]);
        tmb.AddMainMenuButton();

        await client.EditMessageTextAsync(replyData, tmb, ParseMode.Html, cancellationToken: ct);
    }

    private static string LeaderboardRow(LeaderboardRow row, Guid viewerId)
    {
        var place = row.Rank switch
        {
            1 => "🥇",
            2 => "🥈",
            3 => "🥉",
            _ => $"{row.Rank}.",
        };
        // The viewer sees their own name as others do, to know how they look on the board.
        var name = LeaderboardNames.Format(row.NameMode, row.Profile);
        if (row.UserId == viewerId)
            name += $" <b>{Stats.Leaderboard_You}</b>";
        return string.Format(Stats.Leaderboard_Row, place, name, Number(row.Points), Percent(row.Correct, row.Answers));
    }

    private static string Selected(string title, bool selected) => selected ? $"✅ {title}" : title;

    /// <summary>
    /// A Telegram share link: Telegram opens its share dialog with this message and a link to the bot. The link's
    /// start parameter is the UTM label source=share, so users who join through it show up in the funnel.
    /// </summary>
    private static string GetShareUrl(ProgressStats progress, string botUsername)
    {
        var text = progress.Learned switch
        {
            0 => string.Format(Stats.ShareTextNoWords, botUsername),
            1 => string.Format(Stats.ShareTextFirstWord, botUsername),
            _ => string.Format(Stats.ShareText, Number(progress.Learned), botUsername),
        };
        if (progress.StreakDays > 1)
            text += string.Format(Stats.ShareStreak, progress.StreakDays);

        var botLink = $"https://t.me/{botUsername}?start=source-share";
        return $"https://t.me/share/url?url={Uri.EscapeDataString(botLink)}&text={Uri.EscapeDataString(text)}";
    }

    private static int Percent(int part, int total) => total == 0 ? 0 : (int)Math.Round(part * 100.0 / total);

    private static string Number(int value) => value.ToString("N0", CultureInfo.CurrentUICulture);

    public async Task SendAdminStatsAsync(ChatId telegramId, CancellationToken ct = default)
    {
        var stats = await adminRepository.GetStatsAsync(ct);
        
        var tmb = new TelegramMessageBuilder();
        tmb.AppendRow($"<b>{Stats.AdminStats_Title}</b>");
        tmb.AppendRow();
        
        tmb.AppendRow($"<b>{string.Format(Stats.AdminStats_TotalUsers, stats.TotalUsersCount)}</b>");
        foreach (var registeredUsers in stats.RegisteredUsers)
        {
            tmb.AppendRow($"{registeredUsers.Date:d} (+{registeredUsers.Count})");
        }
        
        tmb
            .AppendRow()
            .AppendRow($"<b>Total users started quiz: {stats.UsersHaveAnyQuizCount}</b>")
            .AppendRow($"<b>Total users started quiz this week: {stats.ActiveWeekUsersCount}</b>");
        
        await client.SendTextMessageAsync(telegramId, tmb, parseMode: ParseMode.Html, cancellationToken: ct);
    }
}