using System.Diagnostics.Metrics;
using Laraue.Apps.LearnLanguage.AppServices.Identity;
using Laraue.Apps.LearnLanguage.AppServices.Metrics;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;
using Laraue.Apps.LearnLanguage.Host;
using Laraue.Apps.Identity.Internal.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Laraue.Apps.LearnEnglish.IntegrationTests;

[Collection("IntegrationTest")]
public class MetricsTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddJsonFile("appsettings.json");
        builder
            .AddTelegramOptions("Telegram")
            .AddIdentityServices()
            .AddApplicationServices()
            .AddDatabaseServices("Postgre");
        builder.Services.Replace(
            ServiceDescriptor.Singleton<UserIdentityService.UserIdentityServiceClient, FakeUserIdentityServiceClient>());
        builder.Services.AddSingleton<LearnLanguageStateMetrics>();
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
            await scope.ServiceProvider.GetRequiredService<DatabaseContext>().Users.ExecuteDeleteAsync();
        }

        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task StateMetrics_CountUsersAndActivityByWindow()
    {
        var now = DateTime.UtcNow;
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var language = await db.Languages.OrderBy(x => x.Id).FirstAsync();
            var word = await db.Words.OrderBy(x => x.Id).FirstAsync();
            var translation = await db.Translations.Where(x => x.WordId == word.Id).FirstAsync();

            // Started a quiz and answered a question today.
            var today = NewUser(1, now);
            // Started (and finished) a quiz 10 days ago.
            var tenDaysAgo = NewUser(2, now.AddDays(-40));
            // Learned a word 40 days ago.
            var longAgo = NewUser(3, now.AddDays(-40));
            // Registered, did nothing.
            var idle = NewUser(4, now.AddDays(-40));
            db.Users.AddRange(today, tenDaysAgo, longAgo, idle);
            await db.SaveChangesAsync();

            db.UserQuizzes.Add(new UserQuiz
            {
                UserId = today.Id,
                LanguageId = language.Id,
                Status = UserQuizStatus.Active,
                CreatedAt = now,
                UserQuizQuestions =
                [
                    new UserQuizQuestion { WordId = word.Id, Status = UserQuizQuestionStatus.Correct, AnsweredAt = now },
                ],
            });
            db.UserQuizzes.Add(new UserQuiz
            {
                UserId = tenDaysAgo.Id,
                LanguageId = language.Id,
                Status = UserQuizStatus.Finished,
                CreatedAt = now.AddDays(-10),
                FinishedAt = now.AddDays(-10),
            });
            db.LearnedTranslations.Add(new LearnedTranslation
            {
                UserId = longAgo.Id,
                WordId = translation.WordId,
                LanguageId = translation.LanguageId,
                WinStreakCount = 3,
                LearnedAt = now.AddDays(-40),
            });
            await db.SaveChangesAsync();
        }

        var measurements = await CollectStateAsync();

        Assert.Equal(4, measurements["learnlanguage.users"][""]);
        Assert.Equal(1, measurements["learnlanguage.active_users"]["1d"]);
        Assert.Equal(1, measurements["learnlanguage.active_users"]["7d"]);
        Assert.Equal(2, measurements["learnlanguage.active_users"]["30d"]);
        Assert.Equal(1, measurements["learnlanguage.quizzes_started_in_window"]["1d"]);
        Assert.Equal(2, measurements["learnlanguage.quizzes_started_in_window"]["30d"]);
        Assert.Equal(1, measurements["learnlanguage.quizzes"]["active"]);
        Assert.Equal(1, measurements["learnlanguage.quizzes"]["finished"]);
        Assert.Equal(0, measurements["learnlanguage.quizzes"]["cancelled"]);
        Assert.Equal(1, measurements["learnlanguage.words_learned"][""]);
        Assert.Equal(0, measurements["learnlanguage.words_learned_in_window"]["30d"]);
    }

    private async Task<Dictionary<string, Dictionary<string, long>>> CollectStateAsync()
    {
        var state = _provider.GetRequiredService<LearnLanguageStateMetrics>();
        await state.RefreshAsync(default);

        var result = new Dictionary<string, Dictionary<string, long>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == LearnLanguageMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var label = tags.Length > 0 ? (string)tags[0].Value! : "";
            if (!result.TryGetValue(instrument.Name, out var byLabel))
            {
                result[instrument.Name] = byLabel = new Dictionary<string, long>();
            }

            byLabel[label] = value;
        });
        listener.Start();
        listener.RecordObservableInstruments();

        return result;
    }

    private static User NewUser(long telegramId, DateTime createdAt) => new()
    {
        TelegramId = telegramId,
        GlobalUserId = Guid.NewGuid(),
        CreatedAt = createdAt,
    };
}
