using System.Diagnostics.Metrics;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;

namespace Laraue.Apps.LearnLanguage.AppServices.Metrics;

/// <summary>
/// Language Bot's event counters. Every label has a few values at most: never a user, word or quiz id.
/// State that must survive a restart (how many users there are, how many were active this week) is a gauge
/// read from the database, see <see cref="LearnLanguageStateMetrics"/>. A counter is recorded after the row
/// is saved.
/// </summary>
public sealed class LearnLanguageMetrics
{
    public const string MeterName = "Laraue.Apps.LearnLanguage";

    private readonly Counter<long> _quizzesStarted;
    private readonly Counter<long> _quizzesFinished;
    private readonly Counter<long> _quizAnswers;

    public LearnLanguageMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _quizzesStarted = meter.CreateCounter<long>(
            "learnlanguage.quizzes.started",
            description: "Quizzes started.");

        _quizzesFinished = meter.CreateCounter<long>(
            "learnlanguage.quizzes.finished",
            description: "Quizzes finished (every question answered or skipped, or finished by the user).");

        _quizAnswers = meter.CreateCounter<long>(
            "learnlanguage.quiz.answers",
            description: "Quiz questions answered, by result (correct, incorrect, skipped).");
    }

    public void RecordQuizStarted() => _quizzesStarted.Add(1);

    public void RecordQuizFinished() => _quizzesFinished.Add(1);

    public void RecordQuizAnswer(UserQuizQuestionStatus status)
    {
        if (status == UserQuizQuestionStatus.New)
        {
            return;
        }

        _quizAnswers.Add(1, new KeyValuePair<string, object?>("result", status.ToString().ToLowerInvariant()));
    }
}
