using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Xunit;

namespace Laraue.Apps.LearnEnglish.IntegrationTests;

public class StartControllerTests : IntegrationTest
{
    [Fact]
    public async Task Start_ShouldRegisterUser_WhenStartHasNoPayload()
    {
        using var telegramTestHost = GetTelegramTestHost();

        await telegramTestHost.SendUpdateAsync(new Update
        {
            Message = new Message
            {
                Text = "/start",
                From = DefaultUser,
                Chat = new Chat
                {
                    Id = 1,
                    Type = ChatType.Private,
                }
            }
        });

        Assert.Single(telegramTestHost.GetFromDb(db => db.Users.ToList()));
    }
}
