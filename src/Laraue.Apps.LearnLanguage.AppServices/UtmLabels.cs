namespace Laraue.Apps.LearnLanguage.AppServices;

/// <summary>
/// UTM labels passed in the /start parameter, e.g. "source-share_campaign-autumn": labels separated by
/// <see cref="Separator"/>, each a name and a value separated by <see cref="NameValueSeparator"/>.
/// They're stored for new users, to see in the funnel where they came from.
/// </summary>
public static class UtmLabels
{
    public const char Separator = '_';
    public const char NameValueSeparator = '-';

    /// <summary>
    /// The label telling where a user came from, one of <see cref="UtmSources"/> for links the bot makes.
    /// </summary>
    public const string Source = "source";
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
