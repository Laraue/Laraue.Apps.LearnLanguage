using System.Globalization;
using Laraue.Apps.LearnLanguage.AppServices.Extensions;
using Laraue.Apps.LearnLanguage.AppServices.Metrics;
using Laraue.Apps.LearnLanguage.AppServices.Options;
using Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;
using Laraue.Apps.LearnLanguage.AppServices.Resources;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;
using Laraue.Telegram.NET.Core.Extensions;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Reminders;

/// <summary>
/// Sends the messages that bring users back to practice: the daily reminder for users who turned it on, and,
/// when enabled, a one-time "continue your quiz" message to users who left a quiz unfinished.
/// </summary>
public sealed class ReminderSender(
    DatabaseContext context,
    ITelegramBotClient client,
    IOptions<RemindersOptions> options,
    LearnLanguageMetrics metrics,
    ILogger<ReminderSender> logger)
{
    /// <summary>
    /// A user who practised this recently doesn't need a reminder.
    /// </summary>
    public static readonly TimeSpan PractisedRecently = TimeSpan.FromHours(20);

    /// <summary>
    /// Daily reminders stop after this many in a row went unanswered, and start again after any activity.
    /// </summary>
    public const int MaxIgnoredInRow = 3;

    private static readonly TimeSpan AbandonedAfter = TimeSpan.FromDays(1);

    /// <summary>
    /// The abandoned quiz messages go out in small batches, well within Telegram's limit of ~30 messages a second.
    /// </summary>
    private const int MaxAbandonedQuizRemindersPerRun = 50;

    private static readonly TimeSpan DelayBetweenBatchMessages = TimeSpan.FromMilliseconds(100);

    public async Task SendDueAsync(DateTime now, CancellationToken ct = default)
    {
        await SendDailyRemindersAsync(now, ct);

        if (options.Value.SendAbandonedQuizReminders)
        {
            await SendAbandonedQuizRemindersAsync(now, ct);
        }
    }

    private async Task SendDailyRemindersAsync(DateTime now, CancellationToken ct)
    {
        var timeOfDay = TimeOnly.FromDateTime(now);

        var dueUsers = await context.Users
            .Where(u => u.ReminderTime != null && u.ReminderTime <= timeOfDay && u.BotBlockedAt == null)
            .Select(u => new
            {
                Recipient = new Recipient(u.Id, u.TelegramId, u.TelegramLanguageCode, u.LanguageToLearnId),
                ReminderTime = u.ReminderTime!.Value,
            })
            .ToListAsync(ct);

        foreach (var (recipient, reminderTime) in dueUsers.Select(x => (x.Recipient, x.ReminderTime)))
        {
            var todaySlot = now.Date + reminderTime.ToTimeSpan();

            var sentToday = await context.Reminders.AnyAsync(
                r => r.UserId == recipient.UserId && r.Kind == ReminderKind.Daily && r.SentAt >= todaySlot,
                ct);
            if (sentToday)
            {
                continue;
            }

            var lastActivity = await GetLastActivityAsync(recipient.UserId, ct);
            if (lastActivity > now - PractisedRecently)
            {
                continue;
            }

            // The reminders sent after the last activity are the ones in a row that went unanswered.
            var ignoredInRow = await context.Reminders.CountAsync(
                r => r.UserId == recipient.UserId
                    && r.Kind == ReminderKind.Daily
                    && (lastActivity == null || r.SentAt > lastActivity),
                ct);
            if (ignoredInRow >= MaxIgnoredInRow)
            {
                continue;
            }

            await SendAsync(recipient, ReminderKind.Daily, now, ct);
        }
    }

    private async Task SendAbandonedQuizRemindersAsync(DateTime now, CancellationToken ct)
    {
        var abandonedBefore = now - AbandonedAfter;
        var remindedRecently = now - PractisedRecently;

        var recipients = await context.Users
            .Where(u => u.BotBlockedAt == null)
            .Where(u => context.UserQuizzes.Any(q =>
                q.UserId == u.Id && q.Status == UserQuizStatus.Active && q.CreatedAt < abandonedBefore))
            .Where(u => !context.Reminders.Any(r =>
                r.UserId == u.Id && (r.Kind == ReminderKind.AbandonedQuiz || r.SentAt > remindedRecently)))
            .OrderBy(u => u.CreatedAt)
            .Take(MaxAbandonedQuizRemindersPerRun)
            .Select(u => new Recipient(u.Id, u.TelegramId, u.TelegramLanguageCode, u.LanguageToLearnId))
            .ToListAsync(ct);

        foreach (var recipient in recipients)
        {
            if (await GetLastActivityAsync(recipient.UserId, ct) > now - PractisedRecently)
            {
                continue;
            }

            await SendAsync(recipient, ReminderKind.AbandonedQuiz, now, ct);
            await Task.Delay(DelayBetweenBatchMessages, ct);
        }
    }

    private async Task SendAsync(Recipient recipient, ReminderKind kind, DateTime now, CancellationToken ct)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(InterfaceLanguage.ForCode(recipient.LanguageCode).Code);
        try
        {
            var message = await BuildMessageAsync(recipient, kind, ct);
            await client.SendTextMessageAsync(
                recipient.TelegramId,
                message,
                ParseMode.Html,
                cancellationToken: ct);
        }
        catch (ApiRequestException e) when (e.ErrorCode == 403)
        {
            // The user blocked the bot. Nothing is sent to them on our own until they turn reminders on again.
            await context.Users
                .Where(u => u.Id == recipient.UserId)
                .ExecuteUpdateAsync(u => u.SetProperty(p => p.BotBlockedAt, now), ct);
            logger.LogInformation("User {UserId} blocked the bot, reminders stopped", recipient.UserId);
            return;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Sending a {Kind} reminder to user {UserId} failed", kind, recipient.UserId);
            return;
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }

        context.Reminders.Add(new Reminder { UserId = recipient.UserId, Kind = kind, SentAt = now });
        await context.SaveChangesAsync(ct);
        metrics.RecordReminderSent(kind);
    }

    private async Task<TelegramMessageBuilder> BuildMessageAsync(
        Recipient recipient,
        ReminderKind kind,
        CancellationToken ct)
    {
        var tmb = new TelegramMessageBuilder();

        var activeQuiz = await context.UserQuizzes
            .Where(q => q.UserId == recipient.UserId && q.Status == UserQuizStatus.Active)
            .Select(q => new
            {
                Total = q.UserQuizQuestions.Count,
                Answered = q.UserQuizQuestions.Count(x => x.Status != UserQuizQuestionStatus.New),
            })
            .FirstOrDefaultAsync(ct);

        if (activeQuiz is not null)
        {
            tmb.AppendRow(string.Format(Menu.ReminderContinueQuiz, activeQuiz.Answered + 1, activeQuiz.Total));
            tmb.AddInlineKeyboardButtons([
                new CallbackRoutePath(TelegramRoutes.CurrentQuiz).ToInlineKeyboardButton(Buttons.ContinueQuiz)
            ]);
        }
        else
        {
            // Words the user met but hasn't learned yet: the next quiz brings them back.
            var wordsToReview = await context.LearnedTranslations
                .CountAsync(t => t.UserId == recipient.UserId && t.LearnedAt == null, ct);

            tmb.AppendRow(wordsToReview > 0
                ? string.Format(Menu.ReminderWordsToReview, wordsToReview)
                : Menu.ReminderTimeForQuiz);

            var newQuizRoute = recipient.LanguageToLearnId is not null
                ? new CallbackRoutePath(TelegramRoutes.StartQuiz, RouteMethod.Post)
                    .WithTranslationDirection(new SelectedTranslation(recipient.LanguageToLearnId))
                : new CallbackRoutePath(TelegramRoutes.CurrentQuiz);
            tmb.AddInlineKeyboardButtons([newQuizRoute.ToInlineKeyboardButton(Buttons.RepeatQuiz)]);
        }

        tmb.AddInlineKeyboardButtons([
            kind == ReminderKind.Daily
                ? new CallbackRoutePath(TelegramRoutes.RemindersSettings, RouteMethod.Post)
                    .WithQueryParameter(ParameterNames.Enable, false)
                    .ToInlineKeyboardButton(Buttons.TurnOffReminders)
                : new CallbackRoutePath(TelegramRoutes.RemindersSettings, RouteMethod.Post)
                    .WithQueryParameter(ParameterNames.Enable, true)
                    .ToInlineKeyboardButton(Buttons.RemindMeDaily)
        ]);

        return tmb;
    }

    private async Task<DateTime?> GetLastActivityAsync(Guid userId, CancellationToken ct)
    {
        // The same activity active_users counts: a quiz started, a question answered or a word learned.
        var quizStarted = await context.UserQuizzes
            .Where(q => q.UserId == userId)
            .MaxAsync(q => (DateTime?)q.CreatedAt, ct);
        var questionAnswered = await context.UserQuizQuestions
            .Where(q => q.Quiz.UserId == userId)
            .MaxAsync(q => q.AnsweredAt, ct);
        var wordLearned = await context.LearnedTranslations
            .Where(t => t.UserId == userId)
            .MaxAsync(t => t.LearnedAt, ct);

        return new[] { quizStarted, questionAnswered, wordLearned }.Max();
    }

    private sealed record Recipient(Guid UserId, long TelegramId, string? LanguageCode, long? LanguageToLearnId);
}
