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
    public async Task<IReadOnlyList<EarnedAchievement>> GrantAsync(Guid userId, CancellationToken ct = default)
    {
        // Only quizzes with answers count, so pressing Finish right away earns nothing.
        if (!await AnsweredQuizzes(userId).AnyAsync(ct))
            return [];

        var held = (await context.UserAchievements
                .Where(a => a.UserId == userId)
                .Select(a => new { a.Achievement, a.Level })
                .ToListAsync(ct))
            .Select(a => new AchievementLevel(a.Achievement, a.Level))
            .ToHashSet();

        var now = dateTimeProvider.UtcNow;
        var values = Progress(userId, now, ct);

        var granted = new List<UserAchievement>();
        foreach (var (achievement, definition) in AchievementCatalog.All)
        {
            // Nothing to count when every level is already held.
            var levels = Enumerable.Range(1, definition.Thresholds.Length).Select(l => (byte)l);
            if (levels.All(level => held.Contains(new AchievementLevel(achievement, level))))
                continue;

            var value = await values[achievement]();
            granted.AddRange(definition.LevelsReached(value)
                .Where(level => !held.Contains(new AchievementLevel(achievement, level)))
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
            .Select(g => new AchievementLevel(g.Key, g.Max(a => a.Level)))
            .ToArray();
        return await GetRarityAsync(highest, ct);
    }

    /// <summary>
    /// How to count the user's progress in each achievement: the value compared to its thresholds.
    /// </summary>
    private Dictionary<Achievement, Func<Task<int>>> Progress(Guid userId, DateTime now, CancellationToken ct) =>
        new()
        {
            [Achievement.WordsLearned] = () => CountLearnedWordsAsync(userId, ct),
            [Achievement.QuizzesFinished] = () => AnsweredQuizzes(userId).CountAsync(ct),
            [Achievement.DaysInARow] = () => statsRepository.GetStreakAsync(userId, DateOnly.FromDateTime(now), ct),
            [Achievement.WithoutMistakes] = () => CountPerfectQuizzesInARowAsync(userId, ct),
            // A pair counts once a word is learned in it, not for a quiz only started there.
            [Achievement.LanguagePairs] = () => LearnedTranslations(userId).Select(t => t.LanguageId).Distinct().CountAsync(ct),
            [Achievement.LevelsCompleted] = () => CountLevelsCompletedAsync(userId, ct),
            [Achievement.FirstQuiz] = async () => await AnsweredQuizzes(userId).AnyAsync(ct) ? 1 : 0,
            [Achievement.TenQuizzesInADay] = () => AnsweredQuizzes(userId).CountAsync(q => q.FinishedAt >= now.Date, ct),
            [Achievement.FirstC1Word] = () => LearnedTranslations(userId)
                .Where(t => t.Word.CefrLevel!.Name == C1Level)
                .Select(t => t.WordId)
                .Distinct()
                .CountAsync(ct),
            [Achievement.WelcomeBack] = async () =>
            {
                // The latest quiz came after a week or more without one.
                var lastTwo = await context.UserQuizzes
                    .Where(q => q.UserId == userId)
                    .OrderByDescending(q => q.CreatedAt)
                    .Select(q => q.CreatedAt)
                    .Take(2)
                    .ToListAsync(ct);
                return lastTwo.Count == 2 && lastTwo[0] - lastTwo[1] >= WelcomeBackAfter ? 1 : 0;
            },
            [Achievement.AllWordsLearned] = async () => await HasLearnedAllWordsOfAPairAsync(userId, ct) ? 1 : 0,
        };

    /// <inheritdoc />
    public async Task<AchievementsOverview> GetOverviewAsync(Guid userId, CancellationToken ct = default)
    {
        var heldLevels = await context.UserAchievements
            .Where(a => a.UserId == userId)
            .GroupBy(a => a.Achievement)
            .Select(g => new { Achievement = g.Key, Level = g.Max(a => a.Level) })
            .ToDictionaryAsync(x => x.Achievement, x => x.Level, ct);

        var progress = Progress(userId, dateTimeProvider.UtcNow, ct);
        var items = new List<AchievementProgress>();
        foreach (var achievement in AchievementCatalog.All.Keys)
        {
            // One query at a time: the context isn't thread-safe.
            items.Add(new AchievementProgress(
                achievement,
                heldLevels.GetValueOrDefault(achievement),
                await progress[achievement]()));
        }

        var holders = (await context.UserAchievements
                .GroupBy(a => new { a.Achievement, a.Level })
                .Select(g => new { g.Key.Achievement, g.Key.Level, Count = g.Count() })
                .ToListAsync(ct))
            .ToDictionary(x => new AchievementLevel(x.Achievement, x.Level), x => x.Count);

        return new AchievementsOverview(items, holders, await CountPlayersAsync(ct));
    }

    /// <inheritdoc />
    public Task<int> CountHeldAsync(Guid userId, CancellationToken ct = default) =>
        context.UserAchievements.CountAsync(a => a.UserId == userId, ct);

    /// <inheritdoc />
    public async Task<bool> SetTitleAsync(Guid userId, Achievement? achievement, byte? level, CancellationToken ct = default)
    {
        if (achievement is not null && !await context.UserAchievements.AnyAsync(
                a => a.UserId == userId && a.Achievement == achievement && a.Level == level, ct))
        {
            return false;
        }

        await context.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.TitleAchievement, achievement)
                .SetProperty(x => x.TitleLevel, achievement is null ? null : level), ct);
        return true;
    }

    private Task<int> CountPlayersAsync(CancellationToken ct) => context.UserQuizzes
        .Where(q => q.Status == UserQuizStatus.Finished)
        .Select(q => q.UserId)
        .Distinct()
        .CountAsync(ct);

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
        IReadOnlyCollection<AchievementLevel> achievements,
        CancellationToken ct)
    {
        var kinds = achievements.Select(a => a.Achievement).ToArray();
        var holders = await context.UserAchievements
            .Where(a => kinds.Contains(a.Achievement))
            .GroupBy(a => new { a.Achievement, a.Level })
            .Select(g => new { g.Key.Achievement, g.Key.Level, Count = g.Count() })
            .ToListAsync(ct);
        var players = await CountPlayersAsync(ct);

        return achievements
            .Select(a => new EarnedAchievement(
                a.Achievement,
                a.Level,
                holders.FirstOrDefault(h => h.Achievement == a.Achievement && h.Level == a.Level)?.Count ?? 1,
                players))
            .ToArray();
    }
}
