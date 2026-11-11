namespace Laraue.Apps.LearnLanguage.AppServices;

/// <summary>
/// Links users share to bring others to the bot.
/// </summary>
public static class ShareLinks
{
    /// <summary>
    /// A Telegram share link: Telegram opens its share dialog with <paramref name="text"/> and a link to the
    /// bot that starts it with the UTM label source=<paramref name="utmSource"/>.
    /// </summary>
    public static string Create(string botUsername, string utmSource, string text)
    {
        var botLink = $"https://t.me/{botUsername}?start={UtmLabels.Source}{UtmLabels.NameValueSeparator}{utmSource}";
        return $"https://t.me/share/url?url={Uri.EscapeDataString(botLink)}&text={Uri.EscapeDataString(text)}";
    }
}
