using Laraue.Apps.LearnLanguage.DataAccess;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Laraue.Apps.LearnLanguage.AppServices.Services;

/// <summary>
/// Runs <see cref="DictionarySync"/> once when the host starts, after the migrations. If the sync fails, the
/// exception stops the host, so a broken dictionary file shows up as a failed start.
/// </summary>
public sealed class DictionarySyncService(IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<DictionarySync>()
            .SyncAsync(stoppingToken);
    }
}
