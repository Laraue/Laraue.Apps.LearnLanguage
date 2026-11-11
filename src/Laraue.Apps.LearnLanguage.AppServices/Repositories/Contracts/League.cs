namespace Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;

/// <summary>
/// Leaderboard league: users compete with learners of a similar level.
/// </summary>
public enum League
{
    /// <summary>
    /// Fewer than <see cref="Leagues.IntermediateMinWords"/> learned words.
    /// </summary>
    Beginner,

    Intermediate,
}

public static class Leagues
{
    /// <summary>
    /// Distinct English words learned (in any pair) to be in the <see cref="League.Intermediate"/> league:
    /// about the end of A1 and half of A2.
    /// </summary>
    public const int IntermediateMinWords = 1000;

    public static League For(int learnedWords) =>
        learnedWords >= IntermediateMinWords ? League.Intermediate : League.Beginner;
}
