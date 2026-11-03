namespace Laraue.Apps.LearnLanguage.AppServices;

public static class TelegramRoutes
{
    public const string ViewWordsListMenu = "wwl";

    public const string ListGroupsByCefrLevel = "lgcl";
    public const string DetailGroupByCefrLevel = "dgcl";
    
    public const string ListGroupsByFirstLetter = "lgfl";
    public const string DetailGroupByFirstLetter = "dgfl";
    
    public const string ListGroupsByTopic = "lgt";
    public const string DetailGroupByTopic = "dgt";
    
    public const string CurrentQuiz = "quiz";
    public const string StartQuiz = "sq";
    public const string StartFirstQuiz = "sfq";
    public const string FinishQuiz = "fq";
    public const string SelectQuizAnswer = "sa";
    public const string TopicSelection = "ts";
    public const string CefrLevelSelection = "cs";
    
    public const string Stat = "stat";
    public const string Menu = "m";
    public const string DropMessage = "d";
    
    public const string Settings = "s";
    public const string InterfaceLanguageSettings = "s/ils";
    public const string LearnLanguageSettings = "s/lls";
    
    // Commands section
    public const string Start = "/start(.*)";
}