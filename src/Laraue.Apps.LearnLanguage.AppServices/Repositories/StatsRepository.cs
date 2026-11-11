using Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;
using Laraue.Apps.LearnLanguage.Contracts.Enums;
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

    public async Task<Leaderboard> GetLeaderboardAsync(
        Guid userId,
        League? league,
        DateTime since,
        int count,
        CancellationToken ct = default)
    {
        // Leagues are stored by the weekly league job; the viewer's learned words are counted live, so the
        // words left to the next league go down as they learn.
        var myLeague = await context.Users
            .Where(u => u.Id == userId)
            .Select(u => u.League)
            .FirstAsync(ct);
        var shownLeague = league ?? myLeague;
        var learnedWords = await context.LearnedTranslations
            .Where(t => t.UserId == userId && t.LearnedAt != null)
            .Select(t => t.WordId)
            .Distinct()
            .CountAsync(ct);
        var leagueQuestions = context.UserQuizQuestions.Where(x => x.Quiz.User.League == shownLeague);

        // Points per user of the league who answered in the period. Each query below aggregates it in the
        // database, so only the shown rows come back, however many users play.
        var scores = GetScores(leagueQuestions, since);

        var top = await scores
            .OrderByDescending(x => x.Points)
            .ThenByDescending(x => x.Correct)
            .ThenBy(x => x.UserId)
            .Take(count)
            .ToListAsync(ct);
        var playersCount = top.Count < count ? top.Count : await scores.CountAsync(ct);

        // Ties share a place: the place is 1 + the number of users strictly ahead.
        var ranks = top.ToDictionary(x => x.UserId, x => 1 + top.Count(o => IsAhead(o, x)));
        var me = top.FirstOrDefault(x => x.UserId == userId);
        // The viewer's own row only on their league's board.
        if (me is null && shownLeague == myLeague)
        {
            me = await GetScores(context.UserQuizQuestions.Where(x => x.Quiz.UserId == userId), since)
                .FirstOrDefaultAsync(ct);
            if (me is not null)
            {
                var (points, correct) = (me.Points, me.Correct);
                ranks[me.UserId] = 1 + await scores.CountAsync(
                    x => x.Points > points || (x.Points == points && x.Correct > correct), ct);
            }
        }

        var userIds = ranks.Keys.Append(userId).ToArray();
        var users = await context.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                Profile = new TelegramProfile(u.TelegramUserName, u.TelegramFirstName, u.TelegramLastName),
                u.LeaderboardName,
            })
            .ToDictionaryAsync(u => u.Id, ct);

        LeaderboardRow ToRow(Score score)
        {
            var user = users.GetValueOrDefault(score.UserId);
            return new LeaderboardRow(
                ranks[score.UserId],
                score.UserId,
                user?.Profile ?? new TelegramProfile(null, null, null),
                user?.LeaderboardName ?? LeaderboardNameMode.Hidden,
                score.Correct,
                score.Wrong);
        }

        return new Leaderboard(
            shownLeague,
            myLeague,
            learnedWords,
            top.Select(ToRow).ToArray(),
            me is null ? null : ToRow(me),
            playersCount);
    }

    private static bool IsAhead(Score other, Score score) =>
        other.Points > score.Points || (other.Points == score.Points && other.Correct > score.Correct);

    private static IQueryable<Score> GetScores(IQueryable<UserQuizQuestion> questions, DateTime since) => questions
        .Where(x => x.AnsweredAt >= since)
        .Where(x => x.Status == UserQuizQuestionStatus.Correct || x.Status == UserQuizQuestionStatus.Incorrect)
        .GroupBy(x => x.Quiz.UserId)
        .Select(g => new
        {
            UserId = g.Key,
            Correct = g.Count(x => x.Status == UserQuizQuestionStatus.Correct),
            Wrong = g.Count(x => x.Status == UserQuizQuestionStatus.Incorrect),
        })
        .Select(x => new Score
        {
            UserId = x.UserId,
            Correct = x.Correct,
            Wrong = x.Wrong,
            Points = x.Correct * LeaderboardRow.PointsPerCorrect - x.Wrong * LeaderboardRow.PointsPerWrong,
        });

    // Init properties rather than a constructor, so EF can translate filters and sorting on them.
    private sealed class Score
    {
        public required Guid UserId { get; init; }
        public required int Correct { get; init; }
        public required int Wrong { get; init; }
        public required int Points { get; init; }
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
