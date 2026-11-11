namespace Laraue.Apps.LearnLanguage.AppServices;

public static class TelegramRoutes
{
    public const string CurrentQuiz = "quiz";
    public const string StartQuiz = "sq";
    public const string StartFirstQuiz = "sfq";
    public const string ReviewQuiz = "rvq";
    public const string FinishQuiz = "fq";
    public const string SelectQuizAnswer = "sa";
    public const string TopicSelection = "ts";
    public const string CefrLevelSelection = "cs";
    
    public const string Stat = "stat";
    public const string Leaderboard = "lb";
    public const string Achievements = "achs";
    public const string AchievementTitles = "achs/t";
    public const string Menu = "m";
    public const string DropMessage = "d";
    public const string AchievementMessagesOff = "ach/off";
    
    public const string Settings = "s";
    public const string InterfaceLanguageSettings = "s/ils";
    public const string LearnLanguageSettings = "s/lls";
    public const string RemindersSettings = "s/rem";
    public const string LeaderboardNameSettings = "s/lbn";
    public const string AchievementMessagesSettings = "s/ach";
    
    // Commands section
    public const string Start = "/start(.*)";
}