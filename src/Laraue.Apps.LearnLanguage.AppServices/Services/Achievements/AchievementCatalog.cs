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

    // Levels completed counts CEFR levels from A1 up.
    private static readonly string[] CefrLevels = ["A1", "A2", "B1", "B2", "C1"];

    /// <summary>
    /// Every level of every achievement: what the achievements list counts.
    /// </summary>
    public static int LevelsCount => All.Values.Sum(a => a.Thresholds.Length);

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

    /// <summary>
    /// The level as a heading: "📚 🥇 Scholar", or "👣 First steps" for a single-level achievement.
    /// </summary>
    public static string Heading(Achievement achievement, int level) =>
        $"{All[achievement].Emoji} {Level(achievement, level)}";

    /// <summary>
    /// What the level was earned for: "1,000 words learned".
    /// </summary>
    public static string Description(Achievement achievement, int level) =>
        Text($"{achievement}_Description_{level}");

    /// <summary>
    /// How progress to a level reads, with the progress as {0} and what the level takes as {1}:
    /// "{0} of {1} words". A level can have its own, e.g. for a singular.
    /// </summary>
    public static string ProgressFormat(Achievement achievement, int level) =>
        AchievementTexts.ResourceManager.GetString($"{achievement}_Progress_{level}", AchievementTexts.Culture)
        ?? Text($"{achievement}_Progress");

    /// <summary>
    /// The level's name with the achievement's emoji, as a title on the leaderboard: "🔥 Volcano".
    /// </summary>
    public static string ShortTitle(Achievement achievement, int level) =>
        $"{All[achievement].Emoji} {Text($"{achievement}_{level}")}";

    /// <summary>
    /// The level with its medal, "🥈 Word collector", or just the name of a single-level achievement.
    /// </summary>
    public static string Level(Achievement achievement, int level) =>
        All[achievement].Thresholds.Length == 1
            ? Text($"{achievement}_{level}")
            : $"{Medals[level - 1]} {Text($"{achievement}_{level}")}";

    /// <summary>
    /// The achievement's name; for a single-level one, the name of its level.
    /// </summary>
    public static string Name(Achievement achievement) =>
        All[achievement].Thresholds.Length == 1 ? Text($"{achievement}_1") : Text(achievement.ToString());

    /// <summary>
    /// What a level of an achievement with levels takes: "1,000", or "B1" for levels completed.
    /// </summary>
    public static string Requirement(Achievement achievement, int level) => achievement == Achievement.LevelsCompleted
        ? CefrLevels[level - 1]
        : All[achievement].Thresholds[level - 1].ToString("N0", System.Globalization.CultureInfo.CurrentUICulture);

    /// <summary>
    /// How many players have a level: "4% of players have it", or that nobody (else) has it yet.
    /// </summary>
    public static string Rarity(int holders, int players, bool held)
    {
        if (holders == 0)
            return AchievementTexts.NobodyYet;
        if (held && holders == 1)
            return AchievementTexts.OnlyYou;

        var percent = holders * 100.0 / Math.Max(players, holders);
        return percent < 1
            ? AchievementTexts.RarityUnderOnePercent
            : string.Format(
                AchievementTexts.Rarity,
                Math.Round(percent).ToString(System.Globalization.CultureInfo.CurrentUICulture));
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
