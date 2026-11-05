using Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.LearnLanguage.AppServices.Repositories;

public class StatsRepository(DatabaseContext context) : IStatsRepository
{
    private const int MostMissedOnScreen = 3;
    private const int LastDaysCount = 7;
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    public async Task<ProgressStats?> GetProgressAsync(
        Guid userId,
        long? languageId,
        DateTime now,
        CancellationToken ct = default)
    {
        var practicedPairs = await context.UserQuizzes
            .Where(q => q.UserId == userId)
            .Select(q => q.LanguageId)
            .Distinct()
            .Join(context.Languages, id => id, l => l.Id, (id, l) => new PracticedPair(l.Id, l.Description))
            .ToArrayAsync(ct);

        languageId ??= await context.Users
            .Where(u => u.Id == userId)
            .Select(u => u.LanguageToLearnId)
            .FirstOrDefaultAsync(ct);
        languageId ??= await context.UserQuizzes
            .Where(q => q.UserId == userId)
            .OrderByDescending(q => q.CreatedAt)
            .Select(q => (long?)q.LanguageId)
            .FirstOrDefaultAsync(ct);
        if (languageId is not { } lang)
        {
            return null;
        }

        var languageTitle = await context.Languages
            .Where(l => l.Id == lang)
            .Select(l => l.Description)
            .FirstAsync(ct);

        var weekStart = now - Week;
        var weekQuizzes = await context.UserQuizzes
            .CountAsync(q => q.UserId == userId && q.LanguageId == lang && q.CreatedAt >= weekStart, ct);
        var weekAnswers = await context.UserQuizQuestions
            .Where(x => x.Quiz.UserId == userId && x.Quiz.LanguageId == lang && x.AnsweredAt >= weekStart)
            .Where(x => x.Status == UserQuizQuestionStatus.Correct || x.Status == UserQuizQuestionStatus.Incorrect)
            .GroupBy(x => x.Status == UserQuizQuestionStatus.Correct)
            .Select(g => new { Correct = g.Key, Count = g.Count() })
            .ToArrayAsync(ct);

        var learned = context.LearnedTranslations.Where(t => t.UserId == userId && t.LanguageId == lang);
        var learnedCount = await learned.CountAsync(t => t.LearnedAt != null, ct);
        var inProgressCount = await learned.CountAsync(t => t.LearnedAt == null, ct);
        var total = await context.Translations.CountAsync(t => t.LanguageId == lang, ct);

        var levelTotals = await context.Translations
            .Where(t => t.LanguageId == lang && t.Word.CefrLevelId != null)
            .GroupBy(t => t.Word.CefrLevelId!.Value)
            .Select(g => new { LevelId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.LevelId, x => x.Count, ct);
        var levelLearned = await learned
            .Where(t => t.LearnedAt != null && t.Word.CefrLevelId != null)
            .GroupBy(t => t.Word.CefrLevelId!.Value)
            .Select(g => new { LevelId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.LevelId, x => x.Count, ct);
        var levels = (await context.CefrLevels.OrderBy(l => l.Id).ToArrayAsync(ct))
            .Where(l => levelTotals.ContainsKey(l.Id))
            .Select(l => new CefrLevelProgress(l.Name, levelLearned.GetValueOrDefault(l.Id), levelTotals[l.Id]))
            .ToArray();

        var today = DateOnly.FromDateTime(now);
        var firstDay = today.AddDays(1 - LastDaysCount).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var learnedByDay = await learned
            .Where(t => t.LearnedAt >= firstDay)
            .GroupBy(t => t.LearnedAt!.Value.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => DateOnly.FromDateTime(x.Day), x => x.Count, ct);
        var lastDays = Enumerable.Range(0, LastDaysCount)
            .Select(i => today.AddDays(i + 1 - LastDaysCount))
            .Select(d => new DayLearned(d, learnedByDay.GetValueOrDefault(d)))
            .ToArray();

        return new ProgressStats(
            lang,
            languageTitle,
            practicedPairs,
            await GetStreakAsync(userId, today, ct),
            weekQuizzes,
            weekAnswers.Sum(x => x.Count),
            weekAnswers.Where(x => x.Correct).Sum(x => x.Count),
            learnedCount,
            inProgressCount,
            total,
            levels,
            lastDays,
            await GetMostMissedWordsAsync(userId, lang, MostMissedOnScreen, ct));
    }

    public async Task<MissedWord[]> GetMostMissedWordsAsync(
        Guid userId,
        long languageId,
        int count,
        CancellationToken ct = default)
    {
        var learnedWordIds = context.LearnedTranslations
            .Where(t => t.UserId == userId && t.LanguageId == languageId && t.LearnedAt != null)
            .Select(t => t.WordId);

        var misses = await context.UserQuizQuestions
            .Where(x => x.Quiz.UserId == userId && x.Quiz.LanguageId == languageId)
            .Where(x => x.Status == UserQuizQuestionStatus.Incorrect)
            .Where(x => !learnedWordIds.Contains(x.WordId))
            .GroupBy(x => x.WordId)
            .Select(g => new { WordId = g.Key, Misses = g.Count() })
            .OrderByDescending(x => x.Misses)
            .ThenBy(x => x.WordId)
            .Take(count)
            .ToArrayAsync(ct);

        var wordIds = misses.Select(x => x.WordId).ToArray();
        var texts = await context.Words
            .Where(w => wordIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, w => w.Text, ct);

        return misses.Select(x => new MissedWord(x.WordId, texts[x.WordId], x.Misses)).ToArray();
    }

    /// <summary>
    /// Days in a row with a quiz started or a question answered (UTC), up to today. A streak that ended yesterday
    /// still counts, so it doesn't look broken before the user has practised today.
    /// </summary>
    private async Task<int> GetStreakAsync(Guid userId, DateOnly today, CancellationToken ct)
    {
        var since = today.AddDays(-366).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var quizDays = await context.UserQuizzes
            .Where(q => q.UserId == userId && q.CreatedAt >= since)
            .Select(q => q.CreatedAt.Date)
            .Distinct()
            .ToListAsync(ct);
        var answerDays = await context.UserQuizQuestions
            .Where(x => x.Quiz.UserId == userId && x.AnsweredAt >= since)
            .Select(x => x.AnsweredAt!.Value.Date)
            .Distinct()
            .ToListAsync(ct);
        var days = quizDays.Concat(answerDays).Select(DateOnly.FromDateTime).ToHashSet();

        var day = days.Contains(today) ? today : today.AddDays(-1);
        var streak = 0;
        while (days.Contains(day))
        {
            streak++;
            day = day.AddDays(-1);
        }

        return streak;
    }
}
