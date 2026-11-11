using System.Globalization;

namespace Laraue.Apps.LearnLanguage.AppServices.Services;

/// <summary>
/// How a user's name looks on the leaderboard.
/// </summary>
public static class LeaderboardNames
{
    // At most this many first letters, and always fewer than the whole name.
    private const int ShownLetters = 2;

    // Always the same length, so the mask doesn't tell how long the name is.
    private const string Mask = "*******";

    /// <summary>
    /// The first name, or its first letters followed by a mask when the user hides it. HTML-encoded.
    /// </summary>
    public static string Format(string? firstName, bool show)
    {
        firstName = firstName?.Trim();
        if (string.IsNullOrEmpty(firstName))
            return Mask;

        if (show)
            return Escape(firstName);

        // Text elements, so an emoji or a combined letter isn't cut in half.
        var letters = new StringInfo(firstName);
        // "Al" shows as "A*******", "W" as only the mask: a short name isn't given away whole.
        var prefix = letters.SubstringByTextElements(0, Math.Min(ShownLetters, letters.LengthInTextElements - 1));
        return Escape(prefix) + Mask;
    }

    // Telegram HTML needs only these escaped; WebUtility would also turn emoji into numeric entities.
    private static string Escape(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;");
}
