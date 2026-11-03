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
        await scope.ServiceProvider.GetRequiredService<DictionarySync>().SyncAsync();
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

    [Fact]
    public async Task StateMetrics_CountNewUserFunnelBySource()
    {
        var today = DateTime.UtcNow.Date;
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var language = await db.Languages.OrderBy(x => x.Id).FirstAsync();
            var word = await db.Words.OrderBy(x => x.Id).FirstAsync();

            // Came from "ads", answered, finished a quiz and came back the next day.
            var engaged = NewUser(1, today.AddDays(-2).AddHours(1));
            // No UTM labels, started a quiz the same day but only skipped questions.
            var skipper = NewUser(2, today.AddDays(-3).AddHours(1));
            // Has a label but no source label, did nothing.
            var idle = NewUser(3, today.AddDays(-20));
            // Signed up before both windows.
            var old = NewUser(4, today.AddDays(-40));
            db.Users.AddRange(engaged, skipper, idle, old);
            await db.SaveChangesAsync();

            db.UtmLabels.AddRange(
                new UtmLabel { UserId = engaged.Id, Name = "campaign", Value = "spring" },
                new UtmLabel { UserId = engaged.Id, Name = "source", Value = "ads" },
                new UtmLabel { UserId = idle.Id, Name = "campaign", Value = "x" });

            db.UserQuizzes.AddRange(
                new UserQuiz
                {
                    UserId = engaged.Id,
                    LanguageId = language.Id,
                    Status = UserQuizStatus.Finished,
                    CreatedAt = engaged.CreatedAt.AddMinutes(1),
                    FinishedAt = engaged.CreatedAt.AddMinutes(5),
                    UserQuizQuestions =
                    [
                        new UserQuizQuestion
                        {
                            WordId = word.Id,
                            Status = UserQuizQuestionStatus.Incorrect,
                            AnsweredAt = engaged.CreatedAt.AddMinutes(2),
                        },
                    ],
                },
                new UserQuiz
                {
                    UserId = engaged.Id,
                    LanguageId = language.Id,
                    Status = UserQuizStatus.Active,
                    CreatedAt = engaged.CreatedAt.AddDays(1),
                },
                new UserQuiz
                {
                    UserId = skipper.Id,
                    LanguageId = language.Id,
                    Status = UserQuizStatus.Active,
                    CreatedAt = skipper.CreatedAt.AddMinutes(1),
                    UserQuizQuestions =
                    [
                        new UserQuizQuestion
                        {
                            WordId = word.Id,
                            Status = UserQuizQuestionStatus.Skipped,
                            AnsweredAt = skipper.CreatedAt.AddMinutes(2),
                        },
                    ],
                });
            await db.SaveChangesAsync();
        }

        var funnel = (await CollectStateAsync())["learnlanguage.new_user_funnel"];

        Assert.Equal(1, funnel["7d/signed_up/ads"]);
        Assert.Equal(1, funnel["7d/started_quiz/ads"]);
        Assert.Equal(1, funnel["7d/answered/ads"]);
        Assert.Equal(1, funnel["7d/finished_quiz/ads"]);
        Assert.Equal(1, funnel["7d/returned/ads"]);

        Assert.Equal(1, funnel["7d/signed_up/none"]);
        Assert.Equal(1, funnel["7d/started_quiz/none"]);
        Assert.Equal(0, funnel["7d/answered/none"]);
        Assert.Equal(0, funnel["7d/finished_quiz/none"]);
        Assert.Equal(0, funnel["7d/returned/none"]);

        Assert.DoesNotContain("7d/signed_up/campaign-x", funnel.Keys);
        Assert.Equal(1, funnel["30d/signed_up/campaign-x"]);
        Assert.Equal(0, funnel["30d/started_quiz/campaign-x"]);
        Assert.Equal(3, funnel.Where(x => x.Key.StartsWith("30d/signed_up/")).Sum(x => x.Value));
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
            var label = string.Join("/", tags.ToArray().Select(x => (string)x.Value!));
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
