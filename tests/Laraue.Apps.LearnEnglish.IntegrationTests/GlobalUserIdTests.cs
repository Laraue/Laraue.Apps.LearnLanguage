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
        builder.Configuration["IdentityOptions:BackfillThrottle"] = "00:00:00";
        builder.Configuration["IdentityOptions:BackfillBatchSize"] = "2";
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
            // Restore what the backfill test relaxed.
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE users ALTER COLUMN global_user_id SET NOT NULL");
            await db.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS ix_users_global_user_id ON users (global_user_id)");
        }

        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task NewUser_GetsGlobalIdFromIdentity()
    {
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITelegramUserQueryService<User, Guid>>();

        var id = await service.CreateAsync(
            new User { TelegramId = 100, TelegramUserName = "john", TelegramFirstName = "John", TelegramLanguageCode = "en" },
            default);

        var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == id);
        Assert.Equal(_identity.GlobalIdOf(100), user.GlobalUserId);
        Assert.Equal("john", _identity.Requests.Single().TelegramUsername);
    }

    [Fact]
    public async Task IdentityFailure_DoesNotCreateLocalUser()
    {
        _identity.Fail = true;
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITelegramUserQueryService<User, Guid>>();

        await Assert.ThrowsAsync<RpcException>(() => service.CreateAsync(new User { TelegramId = 101 }, default));

        var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        Assert.False(await db.Users.AnyAsync());
    }

    [Fact]
    public async Task Backfill_FillsOnlyUsersWithoutId_AndIsSafeToRunTwice()
    {
        var existingGlobalId = Guid.NewGuid();
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            // Put the table into the state of the nullable column: no required constraint, no unique index.
            await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS ix_users_global_user_id");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE users ALTER COLUMN global_user_id DROP NOT NULL");

            for (var telegramId = 1; telegramId <= 5; telegramId++)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     INSERT INTO users (id, telegram_id, created_at, global_user_id, words_template_mode, show_words_mode)
                     VALUES ({Guid.NewGuid()}, {telegramId}, now(), {(telegramId == 1 ? existingGlobalId : null)}, 0, 0)
                     """);
            }
        }

        var first = await RunBackfillAsync();
        var second = await RunBackfillAsync();

        Assert.Equal(4, first);
        Assert.Equal(0, second);
        Assert.DoesNotContain(1L, _identity.Requests.Select(r => r.TelegramId));
        Assert.Equal(4, _identity.Requests.Count);

        using var verify = _provider.CreateScope();
        var users = await verify.ServiceProvider.GetRequiredService<DatabaseContext>()
            .Users.AsNoTracking().ToListAsync();
        Assert.Equal(existingGlobalId, users.Single(u => u.TelegramId == 1).GlobalUserId);
        Assert.All(users.Where(u => u.TelegramId != 1), u => Assert.Equal(_identity.GlobalIdOf(u.TelegramId), u.GlobalUserId));
    }

    private async Task<int> RunBackfillAsync()
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<GlobalUserIdBackfill>().RunAsync(default);
    }

    private class RecordingIdentityClient : FakeUserIdentityServiceClient
    {
        private readonly Dictionary<long, Guid> _ids = new();

        public bool Fail { get; set; }

        public List<CreateUserIfNotExistsRequest> Requests { get; } = [];

        public Guid GlobalIdOf(long telegramId) => _ids[telegramId];

        public override AsyncUnaryCall<CreateUserIfNotExistsResponse> CreateUserIfNotExistsAsync(
            CreateUserIfNotExistsRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            if (Fail)
                throw new RpcException(new Status(StatusCode.Unavailable, "Identity is down"));

            Requests.Add(request);
            if (!_ids.ContainsKey(request.TelegramId))
                _ids[request.TelegramId] = Guid.NewGuid();

            return new AsyncUnaryCall<CreateUserIfNotExistsResponse>(
                Task.FromResult(new CreateUserIfNotExistsResponse { UserId = _ids[request.TelegramId].ToString() }),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
        }
    }
}
