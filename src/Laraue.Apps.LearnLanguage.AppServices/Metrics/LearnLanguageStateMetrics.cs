using System.Diagnostics.Metrics;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Laraue.Apps.LearnLanguage.AppServices.Metrics;

/// <summary>
/// Gauges of the bot's state, read from the database: how many users there are, how many were active in the
/// last day, week and month, and how many quizzes were started and words learned in those windows. They answer
/// "what is true now" and survive a restart, which event counters cannot. Refreshed in the background, a scrape
/// only reads the last snapshot. With several replicas each publishes the same numbers, so query them with
/// <c>max()</c>.
/// </summary>
public sealed class LearnLanguageStateMetrics : BackgroundService
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    private static readonly (string Label, TimeSpan Window)[] Windows =
    [
        ("1d", TimeSpan.FromDays(1)),
        ("7d", TimeSpan.FromDays(7)),
        ("30d", TimeSpan.FromDays(30)),
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<LearnLanguageStateMetrics> _logger;

    private volatile Snapshot _snapshot = Snapshot.Empty;

    public LearnLanguageStateMetrics(
        IMeterFactory meterFactory,
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider dateTimeProvider,
        ILogger<LearnLanguageStateMetrics> logger)
    {
        _scopeFactory = scopeFactory;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;

        var meter = meterFactory.Create(LearnLanguageMetrics.MeterName);

        meter.CreateObservableGauge(
            "learnlanguage.users",
            () => new Measurement<long>(_snapshot.Users),
            description: "Users registered in the bot.");

        meter.CreateObservableGauge(
            "learnlanguage.active_users",
            () => Measurements(_snapshot.ActiveUsers, "window"),
            description: "Distinct users who started a quiz, answered a quiz question or learned a word in the last day, 7 days and 30 days.");

        meter.CreateObservableGauge(
            "learnlanguage.quizzes",
            () => Measurements(_snapshot.Quizzes, "status"),
            description: "Quizzes ever started, by status (active, finished, cancelled). The total is the sum.");

        meter.CreateObservableGauge(
            "learnlanguage.quizzes_started_in_window",
            () => Measurements(_snapshot.QuizzesStarted, "window"),
            description: "Quizzes started in the last day, 7 days and 30 days.");

        meter.CreateObservableGauge(
            "learnlanguage.words_learned",
            () => new Measurement<long>(_snapshot.WordsLearned),
            description: "Translations learned by users (answered correctly in a row enough times), all time.");

        meter.CreateObservableGauge(
            "learnlanguage.words_learned_in_window",
            () => Measurements(_snapshot.WordsLearnedInWindow, "window"),
            description: "Translations learned in the last day, 7 days and 30 days.");
    }

    private static IEnumerable<Measurement<long>> Measurements(IReadOnlyDictionary<string, long> values, string label)
        => values.Select(x => new Measurement<long>(x.Value, new KeyValuePair<string, object?>(label, x.Key)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval);

        do
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The gauges keep their last values; the next tick tries again.
                _logger.LogWarning(ex, "Refreshing the Language Bot state metrics failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var now = _dateTimeProvider.UtcNow;

        var users = await context.Users.LongCountAsync(cancellationToken);

        var quizzes = await context.UserQuizzes
            .GroupBy(x => x.Status)
            .Select(x => new { Status = x.Key, Count = x.LongCount() })
            .ToListAsync(cancellationToken);

        var wordsLearned = await context.LearnedTranslations
            .LongCountAsync(x => x.LearnedAt != null, cancellationToken);

        var activeUsers = new Dictionary<string, long>();
        var quizzesStarted = new Dictionary<string, long>();
        var wordsLearnedInWindow = new Dictionary<string, long>();

        foreach (var (label, window) in Windows)
        {
            var since = now - window;

            var quizStarters = context.UserQuizzes
                .Where(x => x.CreatedAt >= since)
                .Select(x => x.UserId);

            var answerers = context.UserQuizQuestions
                .Where(x => x.AnsweredAt >= since)
                .Select(x => x.Quiz.UserId);

            var learners = context.LearnedTranslations
                .Where(x => x.LearnedAt >= since)
                .Select(x => x.UserId);

            activeUsers[label] = await quizStarters
                .Union(answerers)
                .Union(learners)
                .LongCountAsync(cancellationToken);

            quizzesStarted[label] = await context.UserQuizzes
                .LongCountAsync(x => x.CreatedAt >= since, cancellationToken);

            wordsLearnedInWindow[label] = await context.LearnedTranslations
                .LongCountAsync(x => x.LearnedAt >= since, cancellationToken);
        }

        _snapshot = new Snapshot(
            users,
            activeUsers,
            Enum.GetValues<UserQuizStatus>().ToDictionary(
                x => x.ToString().ToLowerInvariant(),
                x => quizzes.SingleOrDefault(q => q.Status == x)?.Count ?? 0),
            quizzesStarted,
            wordsLearned,
            wordsLearnedInWindow);
    }

    private sealed record Snapshot(
        long Users,
        IReadOnlyDictionary<string, long> ActiveUsers,
        IReadOnlyDictionary<string, long> Quizzes,
        IReadOnlyDictionary<string, long> QuizzesStarted,
        long WordsLearned,
        IReadOnlyDictionary<string, long> WordsLearnedInWindow)
    {
        public static readonly Snapshot Empty = new(
            0,
            new Dictionary<string, long>(),
            new Dictionary<string, long>(),
            new Dictionary<string, long>(),
            0,
            new Dictionary<string, long>());
    }
}
