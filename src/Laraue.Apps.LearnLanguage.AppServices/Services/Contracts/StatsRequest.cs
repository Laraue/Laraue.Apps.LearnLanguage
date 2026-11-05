using Laraue.Apps.LearnLanguage.AppServices.Services.LearnModes;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Contracts;

/// <summary>
/// My Progress for a language pair; the default one when not passed.
/// </summary>
public record StatsRequest : WithSelectedTranslationRequest
{
}
