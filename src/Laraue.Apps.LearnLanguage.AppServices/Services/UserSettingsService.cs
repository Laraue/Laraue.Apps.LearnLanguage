using System.Globalization;
using Laraue.Apps.LearnLanguage.AppServices.Extensions;
using Laraue.Apps.LearnLanguage.AppServices.Repositories;
using Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;
using Laraue.Apps.LearnLanguage.AppServices.Resources;
using Laraue.Apps.LearnLanguage.AppServices.Services.Contracts;
using Laraue.Apps.LearnLanguage.Contracts.Enums;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Telegram.NET.Core.Extensions;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Utils;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Laraue.Apps.LearnLanguage.AppServices.Services;

public class UserSettingsService(
    IUserRepository repository,
    ITelegramBotClient client,
    IWordsRepository wordsRepository,
    IDateTimeProvider dateTimeProvider)
    : IUserSettingsService
{
    public async Task HandleSettingsViewAsync(ReplyData replyData, CancellationToken ct = default)
    {
        var settings = await repository.GetSettingsAsync(replyData.UserId, ct);
        var interfaceLanguage = InterfaceLanguage.ForCode(settings.InterfaceLanguageCode); 
        
        var tmb = new TelegramMessageBuilder();

        tmb.AppendRow($"<b>{Buttons.Settings}</b>")
            .AppendRow()
            .AppendRow(string.Format(Settings.CurrentLanguage, $"<b>{interfaceLanguage.Title}</b>"))
            .AppendRow(string.Format(
                Settings.CurrentLearnLanguage,
                settings.LanguageToLearnTitle is not null
                    ? $"<b>English → {settings.LanguageToLearnTitle}</b>"
                    : $"<b>{Settings.NotSet}</b>"))
            .AppendRow(string.Format(
                Settings.Reminders,
                settings.ReminderTime is { } reminderTime
                    ? $"<b>{string.Format(Settings.RemindersOn, reminderTime.ToString("HH:mm"))}</b>"
                    : $"<b>{Settings.RemindersOff}</b>"))
            .AppendRow(string.Format(
                Settings.LeaderboardName,
                LeaderboardNames.GetName(settings.LeaderboardName, settings.Profile) is not null
                    ? $"<b>{LeaderboardNames.Format(settings.LeaderboardName, settings.Profile)}</b>"
                    : $"<b>{Settings.LeaderboardNameHidden}</b>"))
            .AppendRow(string.Format(
                Settings.AchievementMessages,
                $"<b>{(settings.AchievementMessagesOff ? Settings.AchievementMessagesOff : Settings.AchievementMessagesOn)}</b>"))
            .AppendRow();

        tmb.AddInlineKeyboardButtons([
            InlineKeyboardButton.WithCallbackData(
                Buttons.Settings_Language, TelegramRoutes.InterfaceLanguageSettings),
            InlineKeyboardButton.WithCallbackData(
                Buttons.Settings_LearnLanguage, TelegramRoutes.LearnLanguageSettings)
        ]);
        tmb.AddInlineKeyboardButtons([
            new CallbackRoutePath(TelegramRoutes.RemindersSettings, RouteMethod.Post)
                .WithQueryParameter(ParameterNames.Enable, settings.ReminderTime is null)
                .ToInlineKeyboardButton(settings.ReminderTime is null
                    ? Buttons.TurnOnReminders
                    : Buttons.TurnOffReminders)
        ]);

        tmb.AddInlineKeyboardButtons([
            InlineKeyboardButton.WithCallbackData(
                Buttons.Settings_LeaderboardName, TelegramRoutes.LeaderboardNameSettings)
        ]);

        tmb.AddInlineKeyboardButtons([
            new CallbackRoutePath(TelegramRoutes.AchievementMessagesSettings, RouteMethod.Post)
                .WithQueryParameter(ParameterNames.Enable, settings.AchievementMessagesOff)
                .ToInlineKeyboardButton(settings.AchievementMessagesOff
                    ? Buttons.Settings_AchievementMessagesOn
                    : Buttons.Settings_AchievementMessagesOff)
        ]);

        tmb.AddBackMenuButton(TelegramRoutes.Menu);
        
        await client.EditMessageTextAsync(replyData, tmb, ParseMode.Html, cancellationToken: ct);
    }

    public async Task HandleInterfaceLanguageSettingsViewAsync(
        ReplyData replyData,
        UpdateInterfaceLanguageSettingsRequest request,
        CancellationToken ct = default)
    {
        if (request.LanguageCode is not null)
        {
            await repository.SetLanguageCodeAsync(
                replyData.UserId,
                request.LanguageCode,
                ct);

            // Directly change culture to see changes immediately in the current request
            CultureInfo.CurrentCulture = new CultureInfo(request.LanguageCode);
            CultureInfo.CurrentUICulture = new CultureInfo(request.LanguageCode);
            
            await HandleSettingsViewAsync(replyData, ct);
            return;
        }
        
        var tmb = new TelegramMessageBuilder();
        var path = new CallbackRoutePath(TelegramRoutes.InterfaceLanguageSettings);

        tmb
            .AppendRow($"<b>{Settings.UpdateTitle}</b>")
            .AppendRow()
            .AppendRow(Settings.SelectLanguage);

        foreach (var language in InterfaceLanguage.Available)
        {
            var button = path
                .WithQueryParameter(nameof(UpdateInterfaceLanguageSettingsRequest.LanguageCode), language.Code)
                .ToInlineKeyboardButton(language.Title);
        
            tmb.AddInlineKeyboardButtons([button]);
        }

        tmb.AddInlineKeyboardButtons([
            InlineKeyboardButton.WithCallbackData(
                Buttons.Settings_BackButton, TelegramRoutes.Settings)
        ]);
        
        await client.EditMessageTextAsync(replyData, tmb, ParseMode.Html, cancellationToken: ct);
    }

    public async Task UpdateRemindersSettingsAsync(
        ReplyData replyData,
        UpdateRemindersSettingsRequest request,
        CancellationToken ct = default)
    {
        var reminderTime = request.Enabled
            ? TimeOnly.FromDateTime(dateTimeProvider.UtcNow)
            : (TimeOnly?)null;

        await repository.SetReminderTimeAsync(replyData.UserId, reminderTime, ct);
        await HandleSettingsViewAsync(replyData, ct);
    }

    public async Task UpdateAchievementMessagesSettingsAsync(
        ReplyData replyData,
        UpdateAchievementMessagesSettingsRequest request,
        CancellationToken ct = default)
    {
        await repository.SetAchievementMessagesOffAsync(replyData.UserId, !request.Enabled, ct);
        await HandleSettingsViewAsync(replyData, ct);
    }

    public Task TurnOffAchievementMessagesAsync(Guid userId, CancellationToken ct = default)
    {
        return repository.SetAchievementMessagesOffAsync(userId, true, ct);
    }

    public async Task HandleLeaderboardNameSettingsViewAsync(ReplyData replyData, CancellationToken ct = default)
    {
        var settings = await repository.GetSettingsAsync(replyData.UserId, ct);

        var tmb = new TelegramMessageBuilder()
            .AppendRow($"<b>{Settings.UpdateTitle}</b>")
            .AppendRow()
            .AppendRow(Settings.SelectLeaderboardName);

        var path = new CallbackRoutePath(TelegramRoutes.LeaderboardNameSettings, RouteMethod.Post).Freeze();
        var choices = LeaderboardNames.GetChoices(settings.Profile)
            .Prepend((Mode: LeaderboardNameMode.Hidden, Preview: Buttons.LeaderboardName_Hidden));
        foreach (var (mode, preview) in choices)
        {
            var selected = mode == settings.LeaderboardName
                || (mode == LeaderboardNameMode.Hidden && LeaderboardNames.GetName(settings.LeaderboardName, settings.Profile) is null);
            tmb.AddInlineKeyboardButtons([
                path.WithQueryParameter(ParameterNames.LeaderboardName, (int)mode)
                    .ToInlineKeyboardButton(selected ? $"✅ {preview}" : preview)
            ]);
        }

        tmb.AddInlineKeyboardButtons([
            InlineKeyboardButton.WithCallbackData(Buttons.Settings_BackButton, TelegramRoutes.Settings)
        ]);

        await client.EditMessageTextAsync(replyData, tmb, ParseMode.Html, cancellationToken: ct);
    }

    public async Task UpdateLeaderboardNameSettingsAsync(
        ReplyData replyData,
        UpdateLeaderboardNameSettingsRequest request,
        CancellationToken ct = default)
    {
        var mode = Enum.IsDefined((LeaderboardNameMode)request.Mode)
            ? (LeaderboardNameMode)request.Mode
            : LeaderboardNameMode.Hidden;
        await repository.SetLeaderboardNameAsync(replyData.UserId, mode, ct);
        await HandleSettingsViewAsync(replyData, ct);
    }

    public async Task HandleLearnLanguageSettingsViewAsync(ReplyData replyData, CancellationToken ct = default)
    {
        var availableLanguagePairs = await wordsRepository.GetAvailableLearningPairsAsync(ct);
        var path = new CallbackRoutePath(TelegramRoutes.LearnLanguageSettings, RouteMethod.Post)
            .Freeze();
        
        var tmb = new TelegramMessageBuilder();
        
        tmb
            .AppendRow($"<b>{Settings.UpdateTitle}</b>")
            .AppendRow()
            .AppendRow(Settings.SelectLearnLanguage);


        foreach (var pair in availableLanguagePairs)
        {
            tmb.AddInlineKeyboardButtons([path
                .WithQueryParameter(ParameterNames.LanguageToLearn, pair.LanguageToLearn.Id)
                .ToInlineKeyboardButton($"en < - > {pair.LanguageToLearn.Code} ({pair.Count})")]);
        }

        tmb.AddInlineKeyboardButtons([
            path.ToInlineKeyboardButton(Settings.NotSet)
        ]);
        
        tmb.AddInlineKeyboardButtons([
            InlineKeyboardButton.WithCallbackData(
                Buttons.Settings_BackButton, TelegramRoutes.Settings)
        ]);
        
        await client.EditMessageTextAsync(replyData, tmb, ParseMode.Html, cancellationToken: ct);
    }

    public Task UpdateLearnLanguageSettingsAsync(
        ReplyData replyData,
        UpdateLearnLanguageSettingsRequest request,
        CancellationToken ct = default)
    {
        return repository.UpdateLanguageSettingsAsync(
            replyData.UserId,
            new SelectedTranslation(request.LanguageToLearnId),
            ct);
    }
}