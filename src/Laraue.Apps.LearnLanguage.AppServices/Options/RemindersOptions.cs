namespace Laraue.Apps.LearnLanguage.AppServices.Options;

public class RemindersOptions
{
    /// <summary>
    /// Send the one-time "continue your quiz" message to users who left a quiz unfinished. Off by default:
    /// these users never turned reminders on, so it is switched on deliberately, once.
    /// </summary>
    public bool SendAbandonedQuizReminders { get; set; }
}
