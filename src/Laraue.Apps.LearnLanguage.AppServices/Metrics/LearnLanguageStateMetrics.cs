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

    /// <summary>
    /// Sign-up windows of the new user funnel. A day is too short for anyone to come back.
    /// </summary>
    private static readonly (string Label, TimeSpan Window)[] FunnelWindows =
    [
        ("7d", TimeSpan.FromDays(7)),
        ("30d", TimeSpan.FromDays(30)),
    ];

    /// <summary>
    /// The most common sign-up sources get their own label value, the rest are reported as "other".
    /// </summary>
    private const int MaxFunnelSources = 10;

    private const string OtherSource = "other";

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

        meter.CreateObservableGauge(
            "learnlanguage.new_user_funnel",
            () => _snapshot.Funnel.Select(x => new Measurement<long>(
                x.Count,
                new KeyValuePair<string, object?>("window", x.Window),
                new KeyValuePair<string, object?>("step", x.Step),
                new KeyValuePair<string, object?>("source", x.Source))),
            description: "Users who signed up in the last 7 and 30 days, by how far they got: signed_up, " +
                "started_quiz, answered (at least one correct or incorrect answer), finished_quiz (reached the " +
                "results screen), returned (a quiz, an answer or a learned word on a later UTC day than the sign-up). " +
                "source is the user's UTM source, none without one.");
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

        var funnel = await GetFunnelAsync(context, now, cancellationToken);

        _snapshot = new Snapshot(
            users,
            activeUsers,
            Enum.GetValues<UserQuizStatus>().ToDictionary(
                x => x.ToString().ToLowerInvariant(),
                x => quizzes.SingleOrDefault(q => q.Status == x)?.Count ?? 0),
            quizzesStarted,
            wordsLearned,
            wordsLearnedInWindow,
            funnel);
    }

    private static async Task<FunnelMeasurement[]> GetFunnelAsync(
        DatabaseContext context,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var rowsByWindow = new List<(string Window, FunnelRow[] Rows)>();
        foreach (var (label, window) in FunnelWindows)
        {
            rowsByWindow.Add((label, await GetFunnelRowsAsync(context, now - window, cancellationToken)));
        }

        // Keep the label set small and the same in every window: the sources with the most sign-ups in the
        // widest window.
        var topSources = rowsByWindow[^1].Rows
            .OrderByDescending(x => x.SignedUp)
            .ThenBy(x => x.Source)
            .Take(MaxFunnelSources)
            .Select(x => x.Source)
            .ToHashSet();

        return rowsByWindow
            .SelectMany(w => w.Rows
                .GroupBy(x => topSources.Contains(x.Source) ? x.Source : OtherSource)
                .SelectMany(g => new[]
                {
                    new FunnelMeasurement(w.Window, "signed_up", g.Key, g.Sum(x => x.SignedUp)),
                    new FunnelMeasurement(w.Window, "started_quiz", g.Key, g.Sum(x => x.StartedQuiz)),
                    new FunnelMeasurement(w.Window, "answered", g.Key, g.Sum(x => x.Answered)),
                    new FunnelMeasurement(w.Window, "finished_quiz", g.Key, g.Sum(x => x.FinishedQuiz)),
                    new FunnelMeasurement(w.Window, "returned", g.Key, g.Sum(x => x.Returned)),
                }))
            .ToArray();
    }

    private static async Task<FunnelRow[]> GetFunnelRowsAsync(
        DatabaseContext context,
        DateTime since,
        CancellationToken cancellationToken)
    {
        // The source is the "source" or "utm_source" label from the /start payload, otherwise the first label.
        // Activity is what active_users counts: a quiz started, a question answered or a word learned.
        var sql = $$"""
            WITH cohort AS (
                SELECT u.id, u.created_at,
                    COALESCE(
                        (SELECT l.value FROM utm_labels l
                         WHERE l.user_id = u.id AND l.name IN ('source', 'utm_source') AND l.value <> ''
                         ORDER BY l.name LIMIT 1),
                        (SELECT l.name || CASE WHEN l.value <> '' THEN '-' || l.value ELSE '' END FROM utm_labels l
                         WHERE l.user_id = u.id
                         ORDER BY l.name LIMIT 1),
                        'none') AS source
                FROM users u
                WHERE u.created_at >= @since
            ),
            activity AS (
                SELECT q.user_id, q.created_at AS at FROM user_quizzes q
                UNION ALL
                SELECT q.user_id, qq.answered_at FROM user_quiz_questions qq JOIN user_quizzes q ON q.id = qq.quiz_id
                WHERE qq.answered_at IS NOT NULL
                UNION ALL
                SELECT lt.user_id, lt.learned_at FROM learned_translations lt
                WHERE lt.learned_at IS NOT NULL
            )
            SELECT c.source,
                count(*),
                count(*) FILTER (WHERE EXISTS (
                    SELECT 1 FROM user_quizzes q WHERE q.user_id = c.id)),
                count(*) FILTER (WHERE EXISTS (
                    SELECT 1 FROM user_quizzes q JOIN user_quiz_questions qq ON qq.quiz_id = q.id
                    WHERE q.user_id = c.id AND qq.status IN ({{(int)UserQuizQuestionStatus.Correct}}, {{(int)UserQuizQuestionStatus.Incorrect}}))),
                count(*) FILTER (WHERE EXISTS (
                    SELECT 1 FROM user_quizzes q WHERE q.user_id = c.id AND q.status = {{(int)UserQuizStatus.Finished}})),
                count(*) FILTER (WHERE EXISTS (
                    SELECT 1 FROM activity a
                    WHERE a.user_id = c.id AND a.at >= date_trunc('day', c.created_at) + interval '1 day'))
            FROM cohort c
            GROUP BY c.source
            """;

        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "since";
            parameter.Value = since;
            command.Parameters.Add(parameter);

            var rows = new List<FunnelRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new FunnelRow(
                    reader.GetString(0),
                    reader.GetInt64(1),
                    reader.GetInt64(2),
                    reader.GetInt64(3),
                    reader.GetInt64(4),
                    reader.GetInt64(5)));
            }

            return rows.ToArray();
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private sealed record FunnelRow(
        string Source,
        long SignedUp,
        long StartedQuiz,
        long Answered,
        long FinishedQuiz,
        long Returned);

    private sealed record FunnelMeasurement(string Window, string Step, string Source, long Count);

    private sealed record Snapshot(
        long Users,
        IReadOnlyDictionary<string, long> ActiveUsers,
        IReadOnlyDictionary<string, long> Quizzes,
        IReadOnlyDictionary<string, long> QuizzesStarted,
        long WordsLearned,
        IReadOnlyDictionary<string, long> WordsLearnedInWindow,
        IReadOnlyList<FunnelMeasurement> Funnel)
    {
        public static readonly Snapshot Empty = new(
            0,
            new Dictionary<string, long>(),
            new Dictionary<string, long>(),
            new Dictionary<string, long>(),
            0,
            new Dictionary<string, long>(),
            []);
    }
}
