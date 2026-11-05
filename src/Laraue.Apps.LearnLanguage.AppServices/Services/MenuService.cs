using Laraue.Apps.LearnLanguage.AppServices.Extensions;
using Laraue.Apps.LearnLanguage.AppServices.Options;
using Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;
using Laraue.Apps.LearnLanguage.AppServices.Resources;
using Laraue.Apps.LearnLanguage.AppServices.Services.LearnModes;
using Laraue.Apps.LearnLanguage.AppServices.Services.Quiz;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;
using Laraue.Telegram.NET.Core.Extensions;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Utils;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Laraue.Apps.LearnLanguage.AppServices.Services;

public class MenuService(
    ITelegramBotClient client,
    DatabaseContext databaseContext,
    ISelectLanguageService selectLanguageService,
    IQuizService quizService)
    : IMenuService
{
    private const string ReleaseNotesUrl = "https://github.com/win7user10/Laraue.Apps.LearnLanguage/releases";
    
    public Task SendMenuAsync(ReplyData replyData, CancellationToken ct = default)
    {
        var tmb = new TelegramMessageBuilder()
            .AppendRow(Mode.SelectMode);
        
        AppendMainMenuButtons(tmb);

        return client.EditMessageTextAsync(replyData, tmb, ParseMode.Html, cancellationToken: ct);
    }

    public async Task StartAsync(
        string rawCommand,
        Guid userId,
        long telegramId,
        CancellationToken ct = default)
    {
        await ProcessUtmLabels(rawCommand, userId, ct);
        
        // Send welcome message
        var tmb = new TelegramMessageBuilder()
            .AppendRow(Menu.Start);

        var isNewUser = !await databaseContext.UserQuizzes.AnyAsync(q => q.UserId == userId, ct);
        if (isNewUser)
        {
            await AppendFirstQuizButtonsAsync(tmb, userId, ct);
        }
        else
        {
            AppendMainMenuButtons(tmb);
        }

        await client.SendTextMessageAsync(
            telegramId,
            tmb,
            cancellationToken: ct);
    }

    public async Task StartFirstQuizAsync(
        StartQuizRequest request,
        ReplyData replyData,
        CancellationToken ct = default)
    {
        var languageToLearnId = request.LanguageToLearnId!.Value;

        // The language picked here becomes the default pair, so the user isn't asked again.
        await databaseContext.Users
            .Where(u => u.Id == replyData.UserId && u.LanguageToLearnId == null)
            .ExecuteUpdateAsync(u => u
                .SetProperty(p => p.LanguageToLearnId, languageToLearnId), ct);

        await quizService.StartFirstQuizAsync(replyData, languageToLearnId, ct);
    }

    /// <summary>
    /// A new user gets one button to the first question: the first quiz in their default language pair, or a
    /// button per pair when it isn't known. The menu stays available.
    /// </summary>
    private async Task AppendFirstQuizButtonsAsync(
        TelegramMessageBuilder tmb,
        Guid userId,
        CancellationToken ct)
    {
        var languageToLearnId = await databaseContext.Users
            .Where(u => u.Id == userId)
            .Select(u => u.LanguageToLearnId)
            .FirstOrDefaultAsync(ct);

        var route = new CallbackRoutePath(TelegramRoutes.StartFirstQuiz, RouteMethod.Post);

        tmb.AppendRow();
        if (languageToLearnId is not null)
        {
            tmb.AppendRow(string.Format(Menu.FirstQuizOffer, QuizService.FirstQuizQuestionsCount));
            tmb.AddInlineKeyboardButtons([
                InlineKeyboardButton.WithCallbackData(
                    string.Format(Buttons.StartFirstQuiz, QuizService.FirstQuizQuestionsCount),
                    route.WithTranslationDirection(new SelectedTranslation(languageToLearnId)))
            ]);
        }
        else
        {
            tmb.AppendRow(string.Format(Menu.FirstQuizSelectLanguage, QuizService.FirstQuizQuestionsCount));
            await selectLanguageService.AppendLanguagePairButtonsAsync(tmb, route, ct);
        }

        tmb.AddMainMenuButton();
    }

    private async Task ProcessUtmLabels(
        string rawCommand,
        Guid userId,
        CancellationToken ct = default)
    {
        var hasUtmLabels = await databaseContext.UtmLabels
            .Where(l => l.UserId == userId)
            .AnyAsync(ct);

        if (hasUtmLabels)
        {
            return;
        }
        
        // Determine utm labels of the user
        var routeRegex = RouteRegexCreator.ForRoute(TelegramRoutes.Start);
        var match = routeRegex.Match(rawCommand);
        if (match.Groups.Count < 2)
        {
            return;
        }
        
        var queryRaw = match.Groups[1].Value;
        if (string.IsNullOrEmpty(queryRaw))
        {
            return;
        }
        
        var queryParts =  queryRaw.Split('_');
        var dbUtmLabels = queryParts
            .Select(part => part.Split('-'))
            .Select(part => new UtmLabel
            {
                Name = part[0].Trim(),
                Value = part.Length > 1 ? part[1].Trim() : string.Empty,
                UserId = userId,
            })
            .ToArray();
                
        databaseContext.UtmLabels.AddRange(dbUtmLabels);
        await databaseContext.SaveChangesAsync(ct);
    }

    private static void AppendMainMenuButtons(TelegramMessageBuilder telegramMessageBuilder)
    {
        telegramMessageBuilder.AddInlineKeyboardButtons([
                InlineKeyboardButton.WithCallbackData(
                    QuizMode.ButtonName, TelegramRoutes.CurrentQuiz)
            ])
            .AddInlineKeyboardButtons([
                InlineKeyboardButton.WithCallbackData(
                    Buttons.Stat, TelegramRoutes.Stat)
            ])
            .AddInlineKeyboardButtons([
                InlineKeyboardButton.WithCallbackData(
                    Buttons.Settings, TelegramRoutes.Settings)
            ])
            .AddInlineKeyboardButtons([
                InlineKeyboardButton.WithUrl(
                    Buttons.ReleaseNotes, ReleaseNotesUrl)
            ]);
    }
}