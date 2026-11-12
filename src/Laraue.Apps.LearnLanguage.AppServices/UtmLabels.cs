namespace Laraue.Apps.LearnLanguage.AppServices;

/// <summary>
/// UTM labels passed in the /start parameter, e.g. "source-share_campaign-autumn": labels separated by
/// <see cref="Separator"/>, each a name and a value separated by <see cref="NameValueSeparator"/>.
/// Only the <see cref="Source"/> label is used: a new user's source goes to Laraue.Apps.Identity and is kept as
/// <c>User.SignUpSource</c> (MIN-47). The format stays for links that were already shared.
/// </summary>
public static class UtmLabels
{
    public const char Separator = '_';
    public const char NameValueSeparator = '-';

    /// <summary>
    /// The label telling where a user came from, one of <see cref="UtmSources"/> for links the bot makes.
    /// </summary>
    public const string Source = "source";

    private const string StartCommand = "/start";

    /// <summary>
    /// The value of the <see cref="Source"/> label in a <c>/start &lt;payload&gt;</c> message, e.g. "share" for
    /// "/start source-share_campaign-autumn". The value is everything after the first
    /// <see cref="NameValueSeparator"/>, so "source-krop-1" gives "krop-1". Null when the text isn't a /start
    /// command or its payload has no non-empty source label. The raw payload is never returned.
    /// </summary>
    public static string? ParseSource(string? messageText)
    {
        var payload = GetStartPayload(messageText);
        if (string.IsNullOrEmpty(payload))
        {
            return null;
        }

        foreach (var label in payload.Split(Separator))
        {
            var nameValue = label.Split(NameValueSeparator, 2);
            if (nameValue.Length == 2
                && nameValue[0].Trim().Equals(Source, StringComparison.OrdinalIgnoreCase)
                && nameValue[1].Trim() is { Length: > 0 } value)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// The text after "/start" (or "/start@bot"), or null when the text is another message.
    /// </summary>
    private static string? GetStartPayload(string? messageText)
    {
        var text = messageText?.Trim();
        if (text is null || !text.StartsWith(StartCommand, StringComparison.Ordinal))
        {
            return null;
        }

        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0];
        if (command != StartCommand && !command.StartsWith(StartCommand + "@", StringComparison.Ordinal))
        {
            return null;
        }

        return parts.Length > 1 ? parts[1].Trim() : null;
    }
}

/// <summary>
/// Values of the <see cref="UtmLabels.Source"/> label in links the bot makes.
/// </summary>
public static class UtmSources
{
    /// <summary>"Share my progress" on My Progress.</summary>
    public const string Share = "share";

    /// <summary>Share under a new achievement.</summary>
    public const string Achievement = "achievement";

    /// <summary>Share under a trophy.</summary>
    public const string Trophy = "trophy";
}
