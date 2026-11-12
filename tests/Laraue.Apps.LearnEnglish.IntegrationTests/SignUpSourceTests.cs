using Laraue.Telegram.NET.Core.Routing;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Xunit;

namespace Laraue.Apps.LearnEnglish.IntegrationTests;

/// <summary>
/// MIN-47: a new user's first update sends the /start link's source label to Identity.
/// </summary>
public class SignUpSourceTests : IntegrationTest
{
    [Fact]
    public async Task FirstUpdate_ShouldSendSourceLabelToIdentity_WhenStartHasIt()
    {
        var identity = new RecordingIdentityClient();
        using var host = GetTelegramTestHost(identity);

        await host.SendUpdateAsync(TextUpdate("/start source-share_campaign-autumn"));

        Assert.Equal("share", identity.Requests.Single().Source);
    }

    [Theory]
    [InlineData("/start campaign-autumn")]
    [InlineData("/start abc")]
    [InlineData("/start")]
    public async Task FirstUpdate_ShouldSendNoSource_WhenItHasNoSourceLabel(string text)
    {
        var identity = new RecordingIdentityClient();
        using var host = GetTelegramTestHost(identity);

        await host.SendUpdateAsync(TextUpdate(text));

        Assert.False(identity.Requests.Single().HasSource);
        Assert.Single(host.GetFromDb(db => db.Users.ToList()));
    }

    [Fact]
    public async Task FirstUpdate_ShouldSendNoSource_WhenItIsNotStart()
    {
        var identity = new RecordingIdentityClient();
        using var host = GetTelegramTestHost(identity);

        // The user is registered before routing; plain text has no route, which doesn't matter here.
        await Assert.ThrowsAsync<RouteNotFoundException>(() => host.SendUpdateAsync(TextUpdate("source-share")));

        Assert.False(identity.Requests.Single().HasSource);
        Assert.Single(host.GetFromDb(db => db.Users.ToList()));
    }

    private static Update TextUpdate(string text) => new()
    {
        Message = new Message
        {
            Text = text,
            From = DefaultUser,
            Chat = new Chat
            {
                Id = 1,
                Type = ChatType.Private,
            },
        },
    };
}
