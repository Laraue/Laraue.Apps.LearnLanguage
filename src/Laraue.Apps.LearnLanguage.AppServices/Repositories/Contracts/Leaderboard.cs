using Laraue.Apps.LearnLanguage.Contracts.Enums;

namespace Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;

/// <summary>
/// Users of the viewer's league ranked by points for a period, all language pairs together.
/// </summary>
/// <param name="League">The viewer's league, as the league job stored it.</param>
/// <param name="LearnedWords">Distinct words the viewer has learned in any pair, as the league job counted them.</param>
/// <param name="Top">The best users of the league, best first.</param>
/// <param name="Me">The viewer's own row, null when they haven't answered in the period.</param>
/// <param name="PlayersCount">How many users of the league answered in the period.</param>
public record Leaderboard(
    League League,
    int LearnedWords,
    IReadOnlyList<LeaderboardRow> Top,
    LeaderboardRow? Me,
    int PlayersCount);

/// <param name="Rank">1-based place.</param>
/// <param name="UserId">The user the row is about.</param>
/// <param name="Profile">The user's Telegram names.</param>
/// <param name="NameMode">What of <paramref name="Profile"/> others see.</param>
/// <param name="Correct">Right answers in the period.</param>
/// <param name="Wrong">Wrong answers in the period; skipped questions aren't answers.</param>
public record LeaderboardRow(
    int Rank,
    Guid UserId,
    TelegramProfile Profile,
    LeaderboardNameMode NameMode,
    int Correct,
    int Wrong)
{
    /// <summary>
    /// Points for a right answer. With 4 options, random clicking gets 1 right per 3 wrong and averages 0.
    /// </summary>
    public const int PointsPerCorrect = 3;

    /// <summary>
    /// Points taken for a wrong answer.
    /// </summary>
    public const int PointsPerWrong = 1;

    public int Points => Correct * PointsPerCorrect - Wrong * PointsPerWrong;

    public int Answers => Correct + Wrong;
}
