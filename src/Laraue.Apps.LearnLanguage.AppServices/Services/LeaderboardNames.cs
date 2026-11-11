using Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;
using Laraue.Apps.LearnLanguage.Contracts.Enums;

namespace Laraue.Apps.LearnLanguage.AppServices.Services;

/// <summary>
/// How a user's name looks on the leaderboard.
/// </summary>
public static class LeaderboardNames
{
    // Nothing of a hidden name is shown, not even its length.
    public const string Mask = "*******";

    private static readonly LeaderboardNameMode[] ShownModes =
    [
        LeaderboardNameMode.FirstName,
        LeaderboardNameMode.LastName,
        LeaderboardNameMode.FullName,
        LeaderboardNameMode.Username,
    ];

    /// <summary>
    /// The name as others see it, HTML-escaped: the chosen part of the profile, or the mask when the user hides
    /// their name or the profile lacks that part.
    /// </summary>
    public static string Format(LeaderboardNameMode mode, TelegramProfile profile)
    {
        return GetName(mode, profile) is { } name ? Escape(name) : Mask;
    }

    /// <summary>
    /// The modes the user can choose from, with how their name would look in each, unescaped: the ones their
    /// profile has data for.
    /// </summary>
    public static IEnumerable<(LeaderboardNameMode Mode, string Preview)> GetChoices(TelegramProfile profile)
    {
        foreach (var mode in ShownModes)
        {
            // Full name only when it differs from the first or the last one.
            if (mode == LeaderboardNameMode.FullName && (Trim(profile.FirstName) is null || Trim(profile.LastName) is null))
                continue;

            if (GetName(mode, profile) is { } name)
                yield return (mode, name);
        }
    }

    /// <summary>
    /// The chosen part of the profile, or null when it's hidden or missing.
    /// </summary>
    public static string? GetName(LeaderboardNameMode mode, TelegramProfile profile)
    {
        var (first, last, username) = (Trim(profile.FirstName), Trim(profile.LastName), Trim(profile.Username));
        return mode switch
        {
            LeaderboardNameMode.FirstName => first,
            LeaderboardNameMode.LastName => last,
            LeaderboardNameMode.FullName => first is null && last is null ? null : $"{first} {last}".Trim(),
            LeaderboardNameMode.Username => username is null ? null : $"@{username}",
            _ => null,
        };
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Telegram HTML needs only these escaped; WebUtility would also turn emoji into numeric entities.
    private static string Escape(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;");
}
