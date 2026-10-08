using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Core.DataAccess.Linq2DB.Extensions;
using Laraue.Apps.LearnLanguage.AppServices.Metrics;
using Laraue.Telegram.NET.Core.Extensions;
using Laraue.Telegram.NET.Core.Telemetry;
using Microsoft.EntityFrameworkCore;
using Laraue.Apps.LearnLanguage.Host;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

const string dbConnectionStringName = "Postgre";

builder
    .AddTelegramOptions("Telegram")
    .AddIdentityServices()
    .AddApplicationServices()
    .AddDatabaseServices(dbConnectionStringName);

builder.Services.AddHealthChecks();
builder.Services.AddHostedService<LearnLanguageStateMetrics>();
builder.Logging.ClearProviders().AddJsonConsole();

builder.Services
    .AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter(LearnLanguageMetrics.MeterName, LaraueTelegramTelemetry.SourceName)
        .AddPrometheusExporter());

var app = builder.Build();

app.Services.UseLinq2Db();

using (var scope = app.Services.CreateScope())
{
    await using var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

    await db.Database.MigrateAsync();

    app.MapTelegramRequests();
}

app.MapHealthChecks("/_health");
app.MapPrometheusScrapingEndpoint("/_metrics");
app.Run();