namespace Laraue.Apps.LearnLanguage.Contracts.Enums;

/// <summary>
/// An achievement a user can earn, some in several levels (bronze, silver, gold...). Stored by value: don't
/// renumber.
/// </summary>
public enum Achievement : byte
{
    /// <summary>Distinct English words learned: 100 · 500 · 1,000 · 3,000 · 5,000.</summary>
    WordsLearned = 1,

    /// <summary>Quizzes finished with answers: 100 · 1,000 · 10,000.</summary>
    QuizzesFinished = 2,

    /// <summary>Days in a row: 7 · 30 · 100.</summary>
    DaysInARow = 3,

    /// <summary>Quizzes in a row without mistakes: 1 · 5 · 20.</summary>
    WithoutMistakes = 4,

    /// <summary>Language pairs with at least one learned word: 2 · 5.</summary>
    LanguagePairs = 5,

    /// <summary>CEFR levels completed from A1 up: A1 · A2 · B1 · B2 · C1.</summary>
    LevelsCompleted = 6,

    FirstQuiz = 7,

    /// <summary>Quizzes with answers in one day (UTC): 10, 25, 50. Named after its first level.</summary>
    TenQuizzesInADay = 8,

    /// <summary>C1 words learned: 1, 100, 500. Named after its first level.</summary>
    FirstC1Word = 9,

    /// <summary>A quiz after 7 or more days away.</summary>
    WelcomeBack = 10,

    /// <summary>Every word of a language pair learned.</summary>
    AllWordsLearned = 11,
}
