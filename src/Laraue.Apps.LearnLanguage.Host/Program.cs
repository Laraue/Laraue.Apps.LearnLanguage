using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Core.DataAccess.Linq2DB.Extensions;
using Laraue.Telegram.NET.Core.Extensions;
using Microsoft.EntityFrameworkCore;
using Laraue.Apps.LearnLanguage.Host;
using Laraue.Apps.LearnLanguage.AppServices.Identity;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

const string dbConnectionStringName = "Postgre";

builder
    .AddTelegramOptions("Telegram")
    .AddIdentityServices()
    .AddApplicationServices()
    .AddDatabaseServices(dbConnectionStringName);

builder.Services.AddHealthChecks();
builder.Logging.ClearProviders().AddJsonConsole();

builder.Services
    .AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter());

var app = builder.Build();

app.Services.UseLinq2Db();

using (var scope = app.Services.CreateScope())
{
    await using var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

    // GlobalUserId is added nullable, filled for existing users and only then made required, so
    // the migrations are split around the (restartable, no-op once everyone has an id) backfill.
    // TODO(MIN-18): once every environment with users has started on this version, remove this block
    // (back to a plain MigrateAsync), GlobalUserIdBackfill with its options and test, and
    // DatabaseContext.AddGlobalUserIdMigration. Keep both migrations.
    var migrator = db.GetService<IMigrator>();
    await migrator.MigrateAsync(DatabaseContext.AddGlobalUserIdMigration);
    await scope.ServiceProvider.GetRequiredService<GlobalUserIdBackfill>().RunAsync(CancellationToken.None);
    await db.Database.MigrateAsync();

    app.MapTelegramRequests();
}

app.MapHealthChecks("/_health");
app.MapPrometheusScrapingEndpoint("/_metrics");
app.Run();