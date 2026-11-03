using System.Globalization;
using Laraue.Apps.LearnLanguage.AppServices.Services;
using Laraue.Apps.LearnLanguage.Contracts.Enums;
using Xunit;

namespace Laraue.Apps.LearnEnglish.IntegrationTests;

/// <summary>
/// The difficulty labels used to be shuffled between the resource keys in both languages (MIN-31).
/// </summary>
public class DifficultyStringTests
{
    [Theory]
    [InlineData("en", WordTranslationDifficulty.Easy, "easy")]
    [InlineData("en", WordTranslationDifficulty.Medium, "medium")]
    [InlineData("en", WordTranslationDifficulty.Hard, "hard")]
    [InlineData("en", WordTranslationDifficulty.ExtraHard, "very hard")]
    [InlineData("en", WordTranslationDifficulty.Impossible, "impossible")]
    [InlineData("ru", WordTranslationDifficulty.Easy, "легкая")]
    [InlineData("ru", WordTranslationDifficulty.Medium, "средняя")]
    [InlineData("ru", WordTranslationDifficulty.Hard, "сложная")]
    [InlineData("ru", WordTranslationDifficulty.ExtraHard, "очень сложная")]
    [InlineData("ru", WordTranslationDifficulty.Impossible, "невозможная")]
    public void GetDifficultyString_ReturnsLabelOfTheLevel(
        string culture,
        WordTranslationDifficulty difficulty,
        string expected)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try
        {
            Assert.Equal($"A1, {expected}", CommonStrings.GetDifficultyString(difficulty, "A1"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }
}
