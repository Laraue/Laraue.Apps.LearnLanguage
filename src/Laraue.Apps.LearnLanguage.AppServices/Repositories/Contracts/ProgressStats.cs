namespace Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;

/// <summary>
/// A user's progress in one language pair, for the My Progress screen.
/// </summary>
public record ProgressStats(
    long LanguageId,
    string LanguageTitle,
    IReadOnlyList<PracticedPair> PracticedPairs,
    int StreakDays,
    int WeekQuizzes,
    int WeekAnswers,
    int WeekCorrectAnswers,
    int Learned,
    int InProgress,
    int Total,
    IReadOnlyList<CefrLevelProgress> Levels,
    IReadOnlyList<DayLearned> LastDays,
    IReadOnlyList<MissedWord> MostMissed);

public record PracticedPair(long LanguageId, string Title);

public record CefrLevelProgress(string Level, int Learned, int Total);

public record DayLearned(DateOnly Date, int Learned);

public record MissedWord(long WordId, string Word, int Misses);
