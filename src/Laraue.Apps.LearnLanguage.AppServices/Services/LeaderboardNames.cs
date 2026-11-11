namespace Laraue.Apps.LearnLanguage.AppServices.Services;

/// <summary>
/// How a user's name looks on the leaderboard.
/// </summary>
public static class LeaderboardNames
{
    // Nothing of a hidden name is shown, not even its length.
    private const string Mask = "*******";

    /// <summary>
    /// The first name, HTML-escaped, or a mask when the user hides it or has none.
    /// </summary>
    public static string Format(string? firstName, bool show)
    {
        firstName = firstName?.Trim();
        return show && !string.IsNullOrEmpty(firstName)
            ? Escape(firstName)
            : Mask;
    }

    // Telegram HTML needs only these escaped; WebUtility would also turn emoji into numeric entities.
    private static string Escape(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;");
}
