using Laraue.Apps.LearnLanguage.AppServices;
using Xunit;

namespace Laraue.Apps.LearnEnglish.IntegrationTests;

public class UtmLabelsTests
{
    [Theory]
    [InlineData("/start source-share", "share")]
    [InlineData("/start source-share_campaign-autumn", "share")]
    [InlineData("/start campaign-autumn_source-krop1", "krop1")]
    [InlineData("/start source-krop-1", "krop-1")]
    [InlineData("/start Source-Ads", "Ads")]
    [InlineData("  /start   source-share  ", "share")]
    [InlineData("/start@learn_bot source-share", "share")]
    public void ParseSource_ShouldReturnSourceLabelValue_WhenPayloadHasIt(string text, string expected)
    {
        Assert.Equal(expected, UtmLabels.ParseSource(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/start")]
    [InlineData("/start campaign-x")]
    [InlineData("/start abc")]
    [InlineData("/start source")]
    [InlineData("/start source-")]
    [InlineData("/startsource-share")]
    [InlineData("/stats source-share")]
    [InlineData("hello source-share")]
    public void ParseSource_ShouldReturnNull_WhenTextHasNoSourceLabel(string? text)
    {
        Assert.Null(UtmLabels.ParseSource(text));
    }
}
