using System.ComponentModel.DataAnnotations;
using Laraue.Apps.LearnLanguage.Contracts.Enums;

namespace Laraue.Apps.LearnLanguage.DataAccess.Entities;

/// <summary>
/// Application user.
/// </summary>
public sealed class User
{
    /// <summary>
    /// System user identifier.
    /// </summary>
    public Guid Id { get; set; }
    
    /// <summary>
    /// The global Laraue user id from Laraue.Apps.Identity. An additional key, <see cref="Id"/> is still
    /// the one other tables reference.
    /// </summary>
    public Guid GlobalUserId { get; set; }

    /// <summary>
    /// Telegram identifier.
    /// </summary>
    public long TelegramId { get; set; }
    
    /// <summary>
    /// Telegram user name.
    /// </summary>
    [MaxLength(32)]
    public string? TelegramUserName { get; set; }
    
    /// <summary>
    /// Telegram user language code, an IETF language tag, e.g "ru", "en", "pt-br".
    /// </summary>
    [MaxLength(35)]
    public string? TelegramLanguageCode { get; set; }

    /// <summary>
    /// Telegram last name.
    /// </summary>
    [MaxLength(64)]
    public string? TelegramLastName { get; set; }
    
    /// <summary>
    /// Telegram first name.
    /// </summary>
    [MaxLength(64)]
    public string? TelegramFirstName { get; set; }

    /// <summary>
    /// When the user first wrote to the bot (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// UTC time of day for the daily practice reminder, the time the user turned it on. Null when reminders are
    /// off.
    /// </summary>
    public TimeOnly? ReminderTime { get; set; }

    /// <summary>
    /// When Telegram answered that the user blocked the bot. Nothing is sent on our own to such a user.
    /// </summary>
    public DateTime? BotBlockedAt { get; set; }

    /// <summary>
    /// What of the user's names others see on the leaderboard.
    /// </summary>
    public LeaderboardNameMode LeaderboardName { get; set; }

    /// <summary>
    /// The user asked not to get messages about new achievements. They still earn them.
    /// </summary>
    public bool AchievementMessagesOff { get; set; }

    /// <summary>
    /// When the user last finished a quiz whose achievements haven't been checked yet. The achievements job
    /// checks the user and clears it, unless another quiz moved it meanwhile.
    /// </summary>
    public DateTime? AchievementsCheckRequestedAt { get; set; }

    /// <summary>
    /// The achievement whose level name the user shows next to their name on the leaderboard, with
    /// <see cref="TitleLevel"/>. Null for no title.
    /// </summary>
    public Achievement? TitleAchievement { get; set; }

    /// <summary>
    /// The level of <see cref="TitleAchievement"/>.
    /// </summary>
    public byte? TitleLevel { get; set; }

    /// <summary>
    /// How to show word translations for this user. 
    /// </summary>
    public WordsTemplateMode WordsTemplateMode { get; set; }
    
    /// <summary>
    /// Which words should be shown to the user.
    /// </summary>
    public ShowWordsMode ShowWordsMode { get; set; }
    
    /// <summary>
    /// The <see cref="WordLanguage"/> reference.
    /// </summary>
    public long? LanguageToLearnId { get; set; }

    /// <summary>
    /// Default language to learn for the user.
    /// </summary>
    public WordLanguage LanguageToLearn { get; set; } = null!;
    
    /// <summary>
    /// CEFR levels set in the settings.
    /// </summary>
    public ICollection<UserQuizCefrLevel> QuizCefrLevels { get; set; } = null!;
    
    /// <summary>
    /// Topics set in the settings.
    /// </summary>
    public ICollection<UserQuizTopic> UserQuizTopics { get; set; } = null!;
}