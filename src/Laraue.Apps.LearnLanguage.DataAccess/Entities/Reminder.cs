namespace Laraue.Apps.LearnLanguage.DataAccess.Entities;

/// <summary>
/// A message the bot sent on its own to bring the user back to practice.
/// </summary>
public sealed class Reminder : BaseEntity
{
    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public ReminderKind Kind { get; set; }

    public DateTime SentAt { get; set; }
}

public enum ReminderKind
{
    /// <summary>
    /// The daily reminder the user turned on.
    /// </summary>
    Daily,

    /// <summary>
    /// The one-time message to a user who left a quiz unfinished.
    /// </summary>
    AbandonedQuiz,
}
