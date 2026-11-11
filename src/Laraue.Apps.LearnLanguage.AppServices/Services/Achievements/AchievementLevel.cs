using Laraue.Apps.LearnLanguage.Contracts.Enums;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Achievements;

/// <summary>
/// A level of an achievement: 1 for bronze (or a single-level achievement), 2 for silver, and so on.
/// Compared by value, so it can be a key.
/// </summary>
public record AchievementLevel(Achievement Achievement, byte Level);
