using Laraue.Apps.LearnLanguage.AppServices;
using Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Testing;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Xunit;
using User = Laraue.Apps.LearnLanguage.DataAccess.Entities.User;

namespace Laraue.Apps.LearnEnglish.IntegrationTests;

public class QuizControllerTests : IntegrationTest
{
    [Fact]
    public async Task CurrentQuiz_ShouldAskLanguagePair_WhenItIsNotSetInSettings()
    {
        using var telegramTestHost = GetTelegramTestHost();

        await telegramTestHost.SendUpdateAsync(new Update
        {
            CallbackQuery = new CallbackQuery
            {
                From = DefaultUser,
                Data = TelegramRoutes.CurrentQuiz,
                Message = new Message
                {
                    Id = 1
                }
            }
        });

        const string excepted =
"""
<b>🎯 New Quiz</b>

💡 Set a default pair in ⚙️ Settings to skip this step next time.
""";

        var request = telegramTestHost
            .Requests()
            .Single<EditMessageTextRequest>();
        
        request.CheckMessage(excepted);
        request.CheckButtonsSequentially(assert =>
            assert
                .HasButtonsRow(new ButtonAssert("English → German", "quiz?lt=6"))
                .HasButtonsRow(new ButtonAssert("English → Spanish", "quiz?lt=5"))
                .HasButtonsRow(new ButtonAssert("English → French", "quiz?lt=3"))
                .HasButtonsRow(new ButtonAssert("English → Hindi", "quiz?lt=8"))
                .HasButtonsRow(new ButtonAssert("English → Japanese", "quiz?lt=4"))
                .HasButtonsRow(new ButtonAssert("English → Russian", "quiz?lt=2"))
                .HasButtonsRow(new ButtonAssert("English → Chinese", "quiz?lt=7"))
                .HasButtonsRow(new ButtonAssert("Menu", "m")));
    }
    
    [Fact]
    public async Task CurrentQuiz_ShouldOpenStartWindow_WhenLanguageSetInSettings()
    {
        using var telegramTestHost = GetTelegramTestHost();
        telegramTestHost.InsertIntoDb(new User
        {
            TelegramId = DefaultUser.Id,
            LanguageToLearnId = DefaultContextData.GetWordLanguages().GetId("ru")
        });

        await telegramTestHost.SendUpdateAsync(new Update
        {
            CallbackQuery = new CallbackQuery
            {
                From = DefaultUser,
                Data = TelegramRoutes.CurrentQuiz,
                Message = new Message
                {
                    Id = 1
                }
            }
        });

        var request = telegramTestHost
            .Requests()
            .Single<EditMessageTextRequest>();
        
        request.CheckMessage(
"""
<b>🎯 Quiz is ready</b>

Language: <b>English → Russian</b>
Topics: <b>All</b>
CEFR Levels: <b>All (A1-C1)</b>
Questions: <b>10</b>

You progress for these settings
✅ Correct: <b>0</b>
❌ Incorrect: <b>0</b>
⏭ Skipped: <b>0</b>
🔷 Learned: <b>0 / 5948</b> meanings
""");
    }
    
    [Fact]
    public async Task StartQuiz_ShouldStartQuizAndPrintFirstQuestion_Always()
    {
        using var telegramTestHost = GetTelegramTestHost();
        var ruLanguageId = DefaultContextData.GetWordLanguages().GetId("ru");

        await telegramTestHost.SendUpdateAsync(new Update
        {
            CallbackQuery = new CallbackQuery
            {
                From = DefaultUser,
                Data = new CallbackRoutePath(TelegramRoutes.StartQuiz, RouteMethod.Post)
                    .WithTranslationDirection(new SelectedTranslation(ruLanguageId)),
                Message = new Message
                {
                    Id = 1
                }
            }
        });

        var request = telegramTestHost
            .Requests()
            .Single<EditMessageTextRequest>();

        var firstQuestionOfQuiz = telegramTestHost.GetFromDb(db => db
            .UserQuizQuestions
            .OrderBy(q => q.Id)
            .First());

        var questionId = firstQuestionOfQuiz.Id;
        
        request.CheckMessage(
            """
            Question 1/10
            
            <b>a</b>
            indefinite article · A1
            """);
        
        request.CheckButtonsSequentially(buttons => 
            buttons
                .HasButtonsRow(
                    new ButtonAssert("Один", $"1 sa?q={questionId}&a=1"),
                    new ButtonAssert("Покидать", $"1 sa?q={questionId}&a=2"))
                .HasButtonsRow(
                    new ButtonAssert("Способность", $"1 sa?q={questionId}&a=3"),
                    new ButtonAssert("Способный", $"1 sa?q={questionId}&a=4"))
                .HasButtonsRow(new ButtonAssert("⏭ Skip", $"1 sa?q={questionId}&a=0"))
                .HasButtonsRow(new ButtonAssert("❌ Finish quiz", "1 fq"))
                .HasButtonsRow(new ButtonAssert("← Back", "m"))
            );

        var quiz = Assert.Single(telegramTestHost.GetFromDb(x => x.UserQuizzes.ToArray()));
        Assert.Equal(ruLanguageId, quiz.LanguageId);
        Assert.Null(quiz.FinishedAt);

        var quizQuestions = telegramTestHost.GetFromDb(x => x.UserQuizQuestions.ToArray());
        Assert.Equal(10, quizQuestions.Length);
        Assert.All(quizQuestions, q => Assert.Equal(4, q.OptionIds.Length));
    }
}