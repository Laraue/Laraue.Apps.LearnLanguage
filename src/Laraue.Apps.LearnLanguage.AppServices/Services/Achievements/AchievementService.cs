using Laraue.Apps.LearnLanguage.AppServices.Repositories;
using Laraue.Apps.LearnLanguage.Contracts.Enums;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Achievements;

public sealed class AchievementService(
    DatabaseContext context,
    IStatsRepository statsRepository,
    IDateTimeProvider dateTimeProvider)
    : IAchievementService
{
    private const string C1Level = "C1";
    private static readonly TimeSpan WelcomeBackAfter = TimeSpan.FromDays(7);

    /// <inheritdoc />
    public async Task<IReadOnlyList<EarnedAchievement>> GrantAfterQuizAsync(
        Guid userId,
        long quizId,
        CancellationToken ct = default)
    {
        var quiz = await context.UserQuizzes
            .Where(q => q.Id == quizId)
            .Select(q => new
            {
                q.CreatedAt,
                Answered = q.UserQuizQuestions.Count(x =>
                    x.Status == UserQuizQuestionStatus.Correct || x.Status == UserQuizQuestionStatus.Incorrect),
            })
            .FirstAsync(ct);

        // Only quizzes with answers count, so pressing Finish right away earns nothing.
        if (quiz.Answered == 0)
            return [];

        var held = (await context.UserAchievements
                .Where(a => a.UserId == userId)
                .Select(a => new { a.Achievement, a.Level })
                .ToListAsync(ct))
            .Select(a => (a.Achievement, a.Level))
            .ToHashSet();

        var now = dateTimeProvider.UtcNow;
        var values = new Dictionary<Achievement, Func<Task<int>>>
        {
            [Achievement.WordsLearned] = () => CountLearnedWordsAsync(userId, ct),
            [Achievement.QuizzesFinished] = () => AnsweredQuizzes(userId).CountAsync(ct),
            [Achievement.DaysInARow] = () => statsRepository.GetStreakAsync(userId, DateOnly.FromDateTime(now), ct),
            [Achievement.WithoutMistakes] = () => CountPerfectQuizzesInARowAsync(userId, ct),
            [Achievement.LanguagePairs] = () => AnsweredQuizzes(userId).Select(q => q.LanguageId).Distinct().CountAsync(ct),
            [Achievement.LevelsCompleted] = () => CountLevelsCompletedAsync(userId, ct),
            [Achievement.FirstQuiz] = () => Task.FromResult(1),
            [Achievement.TenQuizzesInADay] = () => AnsweredQuizzes(userId).CountAsync(q => q.FinishedAt >= now.Date, ct),
            [Achievement.FirstC1Word] = async () => await LearnedTranslations(userId)
                .AnyAsync(t => t.Word.CefrLevel!.Name == C1Level, ct) ? 1 : 0,
            [Achievement.WelcomeBack] = async () =>
            {
                var previousQuizAt = await context.UserQuizzes
                    .Where(q => q.UserId == userId && q.Id != quizId && q.CreatedAt < quiz.CreatedAt)
                    .MaxAsync(q => (DateTime?)q.CreatedAt, ct);
                return quiz.CreatedAt - previousQuizAt >= WelcomeBackAfter ? 1 : 0;
            },
            [Achievement.AllWordsLearned] = async () => await HasLearnedAllWordsOfAPairAsync(userId, ct) ? 1 : 0,
        };

        var granted = new List<UserAchievement>();
        foreach (var (achievement, definition) in AchievementCatalog.All)
        {
            // Nothing to count when every level is already held.
            var levels = Enumerable.Range(1, definition.Thresholds.Length).Select(l => (byte)l);
            if (levels.All(level => held.Contains((achievement, level))))
                continue;

            var value = await values[achievement]();
            granted.AddRange(definition.LevelsReached(value)
                .Where(level => !held.Contains((achievement, level)))
                .Select(level => new UserAchievement
                {
                    UserId = userId,
                    Achievement = achievement,
                    Level = level,
                    AchievedAt = now,
                }));
        }

        if (granted.Count == 0)
            return [];

        context.UserAchievements.AddRange(granted);
        await context.SaveChangesAsync(ct);

        // Several levels at once (catching up): tell about the highest one.
        var highest = granted
            .GroupBy(a => a.Achievement)
            .Select(g => (Achievement: g.Key, Level: g.Max(a => a.Level)))
            .ToArray();
        return await GetRarityAsync(highest, ct);
    }

    private IQueryable<UserQuiz> AnsweredQuizzes(Guid userId) => context.UserQuizzes
        .Where(q => q.UserId == userId && q.Status == UserQuizStatus.Finished)
        .Where(q => q.UserQuizQuestions.Any(x =>
            x.Status == UserQuizQuestionStatus.Correct || x.Status == UserQuizQuestionStatus.Incorrect));

    private IQueryable<LearnedTranslation> LearnedTranslations(Guid userId) => context.LearnedTranslations
        .Where(t => t.UserId == userId && t.LearnedAt != null);

    private Task<int> CountLearnedWordsAsync(Guid userId, CancellationToken ct) =>
        LearnedTranslations(userId).Select(t => t.WordId).Distinct().CountAsync(ct);

    private async Task<int> CountPerfectQuizzesInARowAsync(Guid userId, CancellationToken ct)
    {
        var maxInARow = AchievementCatalog.All[Achievement.WithoutMistakes].Thresholds.Max();
        var lastQuizzesPerfect = await context.UserQuizzes
            .Where(q => q.UserId == userId && q.Status == UserQuizStatus.Finished)
            .OrderByDescending(q => q.FinishedAt)
            .Take(maxInARow)
            .Select(q => q.UserQuizQuestions.Any()
                && q.UserQuizQuestions.All(x => x.Status == UserQuizQuestionStatus.Correct))
            .ToListAsync(ct);

        return lastQuizzesPerfect.TakeWhile(perfect => perfect).Count();
    }

    /// <summary>
    /// CEFR levels whose every word (of those with a translation) is learned, counted from A1 up.
    /// </summary>
    private async Task<int> CountLevelsCompletedAsync(Guid userId, CancellationToken ct)
    {
        var totals = await context.Words
            .Where(w => w.CefrLevelId != null && w.Translations.Any())
            .GroupBy(w => w.CefrLevelId!.Value)
            .Select(g => new { LevelId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.LevelId, x => x.Count, ct);
        var learned = await LearnedTranslations(userId)
            .Where(t => t.Word.CefrLevelId != null)
            .GroupBy(t => t.Word.CefrLevelId!.Value)
            .Select(g => new { LevelId = g.Key, Count = g.Select(t => t.WordId).Distinct().Count() })
            .ToDictionaryAsync(x => x.LevelId, x => x.Count, ct);
        var levelIds = await context.CefrLevels.OrderBy(l => l.Id).Select(l => l.Id).ToListAsync(ct);

        return levelIds
            .Where(totals.ContainsKey)
            .TakeWhile(id => learned.GetValueOrDefault(id) >= totals[id])
            .Count();
    }

    private async Task<bool> HasLearnedAllWordsOfAPairAsync(Guid userId, CancellationToken ct)
    {
        var learnedByPair = await LearnedTranslations(userId)
            .GroupBy(t => t.LanguageId)
            .Select(g => new { LanguageId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        if (learnedByPair.Count == 0)
            return false;

        var pairIds = learnedByPair.Select(x => x.LanguageId).ToArray();
        var totals = await context.Translations
            .Where(t => pairIds.Contains(t.LanguageId))
            .GroupBy(t => t.LanguageId)
            .Select(g => new { LanguageId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.LanguageId, x => x.Count, ct);

        return learnedByPair.Any(x => x.Count >= totals.GetValueOrDefault(x.LanguageId, int.MaxValue));
    }

    private async Task<IReadOnlyList<EarnedAchievement>> GetRarityAsync(
        IReadOnlyCollection<(Achievement Achievement, byte Level)> achievements,
        CancellationToken ct)
    {
        var kinds = achievements.Select(a => a.Achievement).ToArray();
        var holders = await context.UserAchievements
            .Where(a => kinds.Contains(a.Achievement))
            .GroupBy(a => new { a.Achievement, a.Level })
            .Select(g => new { g.Key.Achievement, g.Key.Level, Count = g.Count() })
            .ToListAsync(ct);
        var players = await context.UserQuizzes
            .Where(q => q.Status == UserQuizStatus.Finished)
            .Select(q => q.UserId)
            .Distinct()
            .CountAsync(ct);

        return achievements
            .Select(a => new EarnedAchievement(
                a.Achievement,
                a.Level,
                holders.FirstOrDefault(h => h.Achievement == a.Achievement && h.Level == a.Level)?.Count ?? 1,
                players))
            .ToArray();
    }
}
