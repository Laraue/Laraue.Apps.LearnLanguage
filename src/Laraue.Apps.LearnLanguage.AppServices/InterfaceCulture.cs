using System.Globalization;

namespace Laraue.Apps.LearnLanguage.AppServices;

/// <summary>
/// Texts sent outside of a user's request (by background jobs) need the user's interface language: the request
/// pipeline sets it otherwise.
/// </summary>
public static class InterfaceCulture
{
    /// <summary>
    /// Switches resources to the user's interface language until disposed.
    /// </summary>
    public static IDisposable Use(string? telegramLanguageCode)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(InterfaceLanguage.ForCode(telegramLanguageCode).Code);
        return new Restore(previous);
    }

    private sealed class Restore(CultureInfo previous) : IDisposable
    {
        public void Dispose() => CultureInfo.CurrentUICulture = previous;
    }
}
