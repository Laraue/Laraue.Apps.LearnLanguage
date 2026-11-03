using Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;
using Laraue.Apps.LearnLanguage.Contracts.Enums;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.LearnLanguage.AppServices.Repositories;

public class UserRepository(DatabaseContext context) : IUserRepository
{
    public Task<UserViewSettings> GetViewSettingsAsync(Guid userId, CancellationToken ct = default)
    {
        return context.Users
            .Where(x => x.Id == userId)
            .Select(x => new UserViewSettings(
                x.WordsTemplateMode,
                x.ShowWordsMode))
            .FirstAsyncEF(ct);
    }

    public async Task UpdateViewSettings(Guid userId, IChangeUserSettingsRequest request, CancellationToken ct = default)
    {
        if (request.ToggleShowTranslations)
        {
            await ToggleWordsTemplateModeAsync(
                userId,
                WordsTemplateMode.HideTranslations,
                ct);
        }
        
        if (request.ToggleRevertTranslations)
        {
            await ToggleWordsTemplateModeAsync(
                userId,
                WordsTemplateMode.RevertWordAndTranslation,
                ct);
        }

        if (request.ShowMode is not null)
        {
            await SetShowWordsModeAsync(
                userId,
                request.ShowMode.GetValueOrDefault(),
                ct);
        }
    }

    public Task<UserSettings> GetSettingsAsync(Guid userId, CancellationToken ct = default)
    {
        return context.Users.Where(x => x.Id == userId)
            .Select(x => new UserSettings(
                x.TelegramLanguageCode,
                x.LanguageToLearnId,
                x.LanguageToLearn.Name,
                x.LanguageToLearn.Description,
                x.ReminderTime))
            .FirstAsyncEF(ct);
    }

    public Task SetReminderTimeAsync(Guid userId, TimeOnly? reminderTime, CancellationToken ct = default)
    {
        // Turning reminders on is also the user saying they want messages from the bot again.
        return context.Users
            .Where(x => x.Id == userId)
            .ExecuteUpdateAsync(x => x
                .SetProperty(u => u.ReminderTime, reminderTime)
                .SetProperty(u => u.BotBlockedAt, u => reminderTime != null ? null : u.BotBlockedAt), ct);
    }

    public Task SetLanguageCodeAsync(Guid userId, string code, CancellationToken ct = default)
    {
        return context.Users
            .Where(x => x.Id == userId)
            .UpdateAsync(_ => new User
            {
                TelegramLanguageCode = code
            }, ct);
    }

    public Task UpdateLanguageSettingsAsync(
        Guid userId,
        SelectedTranslation selectedTranslation,
        CancellationToken ct = default)
    {
        return context.Users
            .Where(x => x.Id == userId)
            .UpdateAsync(_ => new User
            {
                LanguageToLearnId = selectedTranslation.LanguageToLearnId,
            }, ct);
    }

    private Task ToggleWordsTemplateModeAsync(
        Guid userId,
        WordsTemplateMode flagToChange,
        CancellationToken ct = default)
    {
        return context.Users
            .Where(x => x.Id == userId)
            .UpdateAsync(x => new User
            {
                WordsTemplateMode = x.WordsTemplateMode ^ flagToChange
            }, ct);
    }

    private Task SetShowWordsModeAsync(Guid userId, ShowWordsMode value, CancellationToken ct = default)
    {
        return context.Users
            .Where(x => x.Id == userId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.ShowWordsMode, value), ct);
    }
}