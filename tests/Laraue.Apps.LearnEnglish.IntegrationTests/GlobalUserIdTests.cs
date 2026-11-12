using Grpc.Core;
using Laraue.Apps.Identity.Internal.Contracts;
using Laraue.Apps.LearnLanguage.AppServices.Identity;
using Laraue.Telegram.NET.Authentication.Services;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;
using Laraue.Apps.LearnLanguage.Host;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Laraue.Apps.LearnEnglish.IntegrationTests;

[Collection("IntegrationTest")]
public class GlobalUserIdTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;
    private RecordingIdentityClient _identity = null!;

    public async Task InitializeAsync()
    {
        _identity = new RecordingIdentityClient();

        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddJsonFile("appsettings.json");
        builder
            .AddTelegramOptions("Telegram")
            .AddIdentityServices()
            .AddApplicationServices()
            .AddDatabaseServices("Postgre");
        builder.Services.Replace(ServiceDescriptor.Singleton<UserIdentityService.UserIdentityServiceClient>(_identity));
        _provider = builder.Services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        await db.Database.MigrateAsync();
        await db.Users.ExecuteDeleteAsync();
    }

    public async Task DisposeAsync()
    {
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            await db.Users.ExecuteDeleteAsync();
        }

        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task NewUser_GetsGlobalIdFromIdentity()
    {
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITelegramUserQueryService<Guid>>();

        var id = await service.CreateAsync(
            new TelegramData(100, "john", "en", "John", null),
            default);

        var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == id);
        Assert.Equal(_identity.GlobalIdOf(100), user.GlobalUserId);
        Assert.Equal("john", _identity.Requests.Single().TelegramUsername);
    }

    [Fact]
    public async Task CreateAsync_ShouldSendNoSource_WhenThereIsNoUpdate()
    {
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITelegramUserQueryService<Guid>>();

        await service.CreateAsync(new TelegramData(102, null, null, null, null), default);

        Assert.False(_identity.Requests.Single().HasSource);
    }

    [Fact]
    public async Task IdentityFailure_DoesNotCreateLocalUser()
    {
        _identity.Fail = true;
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITelegramUserQueryService<Guid>>();

        await Assert.ThrowsAsync<RpcException>(() => service.CreateAsync(new TelegramData(101, null, null, null, null), default));

        var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        Assert.False(await db.Users.AnyAsync());
    }
}
