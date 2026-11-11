using Laraue.Apps.LearnLanguage.Contracts.Enums;
using AchievementTexts = Laraue.Apps.LearnLanguage.AppServices.Resources.Achievements;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Achievements;

/// <summary>
/// What each achievement takes and how it's called. Names live in the Achievements resources: the achievement
/// as "{Achievement}" and each level as "{Achievement}_{Level}".
/// </summary>
public static class AchievementCatalog
{
    public static readonly IReadOnlyDictionary<Achievement, AchievementDefinition> All =
        new AchievementDefinition[]
        {
            new(Achievement.WordsLearned, "📚", [100, 500, 1000, 3000, 5000]),
            new(Achievement.QuizzesFinished, "🧩", [100, 1000, 10000]),
            new(Achievement.DaysInARow, "🔥", [7, 30, 100]),
            new(Achievement.WithoutMistakes, "🎯", [1, 5, 20]),
            new(Achievement.LanguagePairs, "🌍", [2, 5]),
            // Levels completed from A1 up: A1, A2, B1, B2, C1.
            new(Achievement.LevelsCompleted, "🎓", [1, 2, 3, 4, 5]),
            new(Achievement.FirstQuiz, "👣", [1]),
            new(Achievement.TenQuizzesInADay, "⚡", [10]),
            new(Achievement.FirstC1Word, "🔤", [1]),
            new(Achievement.WelcomeBack, "👋", [1]),
            new(Achievement.AllWordsLearned, "🌟", [1]),
        }
        .ToDictionary(x => x.Achievement);

    private static readonly string[] Medals = ["🥉", "🥈", "🥇", "💎", "👑"];

    /// <summary>
    /// "📚 Words learned · 🥈 Word collector", or "👣 First steps" for a single-level achievement.
    /// </summary>
    public static string Title(Achievement achievement, int level)
    {
        var definition = All[achievement];
        var levelName = Text($"{achievement}_{level}");
        return definition.Thresholds.Length == 1
            ? $"{definition.Emoji} {levelName}"
            : $"{definition.Emoji} {Text(achievement.ToString())} · {Medals[level - 1]} {levelName}";
    }

    private static string Text(string key) =>
        AchievementTexts.ResourceManager.GetString(key, AchievementTexts.Culture) ?? key;
}

/// <param name="Achievement">The achievement.</param>
/// <param name="Emoji">Shown before its name.</param>
/// <param name="Thresholds">What each level takes, ascending; one value for a single-level achievement.</param>
public record AchievementDefinition(Achievement Achievement, string Emoji, int[] Thresholds)
{
    /// <summary>
    /// The levels reached with <paramref name="value"/>: 1 for the first threshold, and so on.
    /// </summary>
    public IEnumerable<byte> LevelsReached(int value) => Enumerable.Range(1, Thresholds.Length)
        .Where(level => value >= Thresholds[level - 1])
        .Select(level => (byte)level);
}
