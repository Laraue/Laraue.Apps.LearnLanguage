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
    public static string Create(string botUsername, string utmSource, string text) =>
        $"https://t.me/share/url?url={Uri.EscapeDataString(BotLink(botUsername, utmSource))}" +
        $"&text={Uri.EscapeDataString(text)}";

    /// <summary>
    /// A Telegram share link with <paramref name="text"/> first and the tracked link to the bot after it.
    /// Telegram's share dialog puts its url parameter before the text, so both go into the url parameter, which
    /// it takes as the whole message (MIN-52).
    /// </summary>
    public static string CreateWithLinkAtEnd(string botUsername, string utmSource, string text) =>
        $"https://t.me/share/url?url={Uri.EscapeDataString($"{text}\n{BotLink(botUsername, utmSource)}")}";

    /// <summary>
    /// A link that starts the bot with the UTM label source=<paramref name="utmSource"/>.
    /// </summary>
    private static string BotLink(string botUsername, string utmSource) =>
        $"https://t.me/{botUsername}?start={UtmLabels.Source}{UtmLabels.NameValueSeparator}{utmSource}";
}
