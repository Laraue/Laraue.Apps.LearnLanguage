using Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;
using Laraue.Telegram.NET.Core.Routing;

namespace Laraue.Apps.LearnLanguage.AppServices;

public static class RoutePathBuilderExtensions
{
    public static CallbackRoutePath WithTranslationDirection(
        this CallbackRoutePath routePath,
        SelectedTranslation selectedTranslation)
    {
        return routePath
            .WithQueryParameter(ParameterNames.LanguageToLearn, selectedTranslation.LanguageToLearnId);
    }
}