using System.Text;
using Laraue.Apps.LearnLanguage.AppServices.Extensions;
using Laraue.Apps.LearnLanguage.AppServices.Metrics;
using Laraue.Apps.LearnLanguage.AppServices.Repositories.Contracts;
using Laraue.Apps.LearnLanguage.AppServices.Resources;
using Laraue.Apps.LearnLanguage.AppServices.Services.LearnModes;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Telegram.NET.Core.Extensions;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Utils;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Laraue.Apps.LearnLanguage.AppServices.Services.Quiz;

public class QuizService(
    QuizService.IRepository repository,
    ISelectLanguageService selectLanguageService,
    ITelegramBotClient client,
    DatabaseContext context,
    IQuestionsGenerator questionsGenerator,
    LearnLanguageMetrics metrics)
    : IQuizService
{
    private const long NullOptionId = 0;
    private const int WinStreakToLearn = 3;
    private const int MaxTopicsCount = 30;
    
    // TODO - use settings to setup
    private const int QuestionsCount = 20;
    private const int OptionsCount = 8;
    
    /// <summary>
    /// Handle any request related to quiz. If quiz is started handle answers, otherwise handle new quiz window.
    /// </summary>
    public async Task OpenQuizWindowAsync(
        ReplyData replyData,
        QuizRequest request,
        CancellationToken ct = default)
    {
        var hasActiveQuiz = await HasActiveQuizAsync(replyData.UserId, ct);
        
        var task = hasActiveQuiz
            ? OpenNextQuizQuestionWindowAsync(
                replyData,
                previousAnswerResult: null,
                ct)
            : OpenNewQuizWindowAsync(
                replyData,
                request,
                ct);

        await task;
    }

    public async Task ChangeTopicAsync(
        ReplyData replyData,
        ChangeTopicRequest request,
        CancellationToken ct = default)
    {
        await repository.UpdateTopicAsync(
            replyData.UserId,
            request.TopicId,
            request.Enable,
            ct);

        await OpenSelectTopicWindowAsync(
            replyData,
            new SelectTopicRequest { LanguageToLearnId = request.LanguageToLearnId },
            ct);
    }

    public async Task OpenSelectCefrLevelWindowAsync(
        ReplyData replyData,
        SelectCefrLevelRequest request,
        CancellationToken ct = default)
    {
        var topics = await repository.GetUserQuizTopicsAsync(replyData.UserId, ct);
        var cefrLevels = await repository.GetUserSelectedCefrLevelsAsync(
            replyData.UserId,
            topics.Select(x => x.Id).ToArray(),
            ct);

        var activeCefrLevels = cefrLevels
            .Where(x => x.IsSelected)
            .ToArray();

        var cefrLevelNames = activeCefrLevels.Length > 0
            ? string.Join(" · ", activeCefrLevels.Select(x => x.Name))
            : QuizMode.All;
        
        var tmb = new TelegramMessageBuilder()
            .AppendRow(QuizMode.CefrLevelWithIcon)
            .AppendRow(string.Format(QuizMode.Selected, $"<b>{cefrLevelNames}</b>"));
        
        var buttons = new List<InlineKeyboardButton>();
        
        foreach (var cefrLevel in cefrLevels)
        {
            var buttonTextBuilder = new StringBuilder();
            if (cefrLevel.IsSelected)
                buttonTextBuilder.Append("✅ ");

            buttonTextBuilder.Append($"{cefrLevel.Name} ({cefrLevel.WordsCount})");
            
            var button = new CallbackRoutePath(TelegramRoutes.CefrLevelSelection, RouteMethod.Post)
                .WithQueryParameter(ParameterNames.CefrLevelId, cefrLevel.Id)
                .WithQueryParameter(ParameterNames.Enable, !cefrLevel.IsSelected)
                .WithTranslationDirection(request)
                .ToInlineKeyboardButton(buttonTextBuilder.ToString());
                
            buttons.Add(button);
        }
        
        foreach (var buttonsRow in buttons.Chunk(2))
            tmb.AddInlineKeyboardButtons(buttonsRow);
        
        tmb
            .AddBackMenuButton(new CallbackRoutePath(TelegramRoutes.CurrentQuiz)
                .WithTranslationDirection(request))
            .AddMainMenuButton();
        
        await client.EditMessageTextAsync(replyData, tmb, ParseMode.Html, cancellationToken: ct);
    }

    public async Task ChangeCefrLevelAsync(
        ReplyData replyData,
        ChangeCefrLevelRequest request,
        CancellationToken ct = default)
    {
        await repository.UpdateCefrLevelAsync(
            replyData.UserId,
            request.CefrLevelId,
            request.Enable,
            ct);

        await OpenSelectCefrLevelWindowAsync(
            replyData,
            new SelectCefrLevelRequest { LanguageToLearnId = request.LanguageToLearnId },
            ct);
    }

    private string GetTopicNamesString(TopicItemDto[] topicItems)
    {
        var topicNames = QuizMode.All;
        
        if (topicItems.Length > 0)
        {
            const int maxTopicsInTitle = 5;
            var topicsCountMoreThanAllowed = topicItems.Length > maxTopicsInTitle;
            var topicsForMessage = topicItems.Take(maxTopicsInTitle).ToArray();

            var topicNamesBuilder = new StringBuilder();
            topicNamesBuilder.AppendJoin(" · ", topicsForMessage.Select(x => x.Name));
            if (topicsCountMoreThanAllowed)
                topicNamesBuilder
                    .Append(' ')
                    .Append(string.Format(QuizMode.AndMore, topicItems.Length - maxTopicsInTitle));

            topicNames = topicNamesBuilder.ToString();
        }

        return topicNames;
    }

    public async Task OpenSelectTopicWindowAsync(
        ReplyData replyData,
        SelectTopicRequest request,
        CancellationToken ct = default)
    {
        var cefrLevels = await repository
            .GetUserCefrLevelsAsync(replyData.UserId, ct);
        
        var topics = await repository.GetTopicsAsync(
            MaxTopicsCount,
            replyData.UserId,
            cefrLevels.Select(x => x.Id).ToArray(),
            ct);
        
        var activeTopics = topics
            .Where(t => t.IsSelected)
            .Cast<TopicItemDto>()
            .ToArray();

        var topicNames = GetTopicNamesString(activeTopics);
        
        var tmb = new TelegramMessageBuilder()
            .AppendRow(QuizMode.TopicsWithIcon)
            .AppendRow(string.Format(QuizMode.Selected, $"<b>{topicNames}</b>"));

        var buttons = new List<InlineKeyboardButton>();
        foreach (var topic in topics)
        {
            var buttonTextBuilder = new StringBuilder();
            if (topic.IsSelected)
                buttonTextBuilder.Append("✅ ");

            buttonTextBuilder.Append($"{topic.Name} ({topic.WordsCount})");
            
            buttons.Add(new CallbackRoutePath(TelegramRoutes.TopicSelection, RouteMethod.Post)
                .WithQueryParameter(ParameterNames.TopicId, topic.Id)
                .WithQueryParameter(ParameterNames.Enable, !topic.IsSelected)
                .WithTranslationDirection(request)
                .ToInlineKeyboardButton(buttonTextBuilder.ToString()));
        }

        foreach (var buttonsRow in buttons.Chunk(2))
            tmb.AddInlineKeyboardButtons(buttonsRow);
        
        tmb
            .AddBackMenuButton(new CallbackRoutePath(TelegramRoutes.CurrentQuiz)
                .WithTranslationDirection(request))
            .AddMainMenuButton();
        
        await client.EditMessageTextAsync(replyData, tmb, ParseMode.Html, cancellationToken: ct);
    }

    /// <summary>
    /// The start quiz logic.
    /// </summary>
    public async Task StartNewQuizAsync(
        ReplyData replyData,
        StartQuizRequest startQuizRequest,
        CancellationToken ct = default)
    {
        if (!await HasActiveQuizAsync(replyData.UserId, ct))
        {
            await using var transaction = await context.Database.BeginTransactionAsync(ct);

            var topicData = await repository.GetUserQuizTopicsAsync(replyData.UserId, ct);
            var cefrLevelData = await repository.GetUserCefrLevelsAsync(replyData.UserId, ct);
            
            var quizId = await repository.CreateQuizAsync(
                replyData.UserId,
                startQuizRequest.LanguageToLearnId!.Value,
                ct);
        
            var questions = await questionsGenerator.GenerateQuestions(
                replyData.UserId,
                startQuizRequest.LanguageToLearnId!.Value,
                topicData.Select(x => x.Id).ToArray(),
                cefrLevelData.Select(x => x.Id).ToArray(),
                QuestionsCount,
                OptionsCount,
                ct);

            await repository.SaveQuizQuestionsAsync(quizId, questions, ct);
        
            await transaction.CommitAsync(ct);

            metrics.RecordQuizStarted();
        }
        
        await OpenNextQuizQuestionWindowAsync(
            replyData,
            previousAnswerResult: null,
            ct);
    }

    public async Task FinishQuizAsync(ReplyData replyData, CancellationToken ct = default)
    {
        if (!await HasActiveQuizAsync(replyData.UserId, ct))
        {
            // The case when the quiz finished but something went wrong
            await OpenNewQuizWindowAsync(
                replyData,
                new QuizRequest(),
                ct);
            
            return;
        }
        
        await repository.SkipAllQuizQuestions(replyData.UserId, ct);
        await OpenNextQuizQuestionWindowAsync(
            replyData,
            previousAnswerResult: null,
            ct);
    }
    
    public async Task SelectQuizAnswerAsync(
        ReplyData replyData,
        SelectQuizAnswerRequest request,
        CancellationToken ct = default)
    {
        if (!await HasActiveQuizAsync(replyData.UserId, ct))
        {
            return;
        }
        
        var result = await HandleSelectedOptionAsync(
            replyData.UserId,
            request.QuestionId,
            request.SelectedOptionId,
            ct);

        await OpenNextQuizQuestionWindowAsync(
            replyData,
            result,
            ct);
    }

    private Task<bool> HasActiveQuizAsync(Guid userId, CancellationToken ct)
    {
        return repository.HasActiveQuizAsync(userId, ct);
    }

    /// <summary>
    /// Before start a new quiz ask user to select language pair for this quiz (or take it from settings if set).
    /// </summary>
    private Task OpenNewQuizWindowAsync(
        ReplyData replyData,
        QuizRequest request,
        CancellationToken ct = default)
    {
        return selectLanguageService.ShowLanguageWindowOrHandleRequestAsync(
            request,
            QuizMode.ButtonName,
            TelegramRoutes.CurrentQuiz,
            replyData,
            OpenBeforeQuizStartWindowAsync,
            ct);
    }

    /// <summary>
    /// Draw a window from which the quiz can be launched or options can be changed.
    /// </summary>
    private async Task OpenBeforeQuizStartWindowAsync(
        QuizRequest request,
        ReplyData replyData,
        SelectedTranslation selectedTranslation,
        CancellationToken ct = default)
    {
        var tmb = new TelegramMessageBuilder();
        
        var topics = await repository.GetUserQuizTopicsAsync(replyData.UserId, ct);
        var topicIds = topics.Select(x => x.Id).ToArray();
        var cefrLevels = await repository.GetUserCefrLevelsAsync(replyData.UserId, ct);
        var cefrLevelIds = cefrLevels.Select(x => x.Id).ToArray();
        
        var languageName = await repository.GetLanguageNameAsync(
            selectedTranslation.LanguageToLearnId!.Value,
            ct);

        var dbQuestionsCount = await repository.GetQuestionsCountByFilterAsync(
            selectedTranslation.LanguageToLearnId!.Value,
            topicIds,
            cefrLevelIds,
            ct);

        var learnStat = await repository.GetLearnStatAsync(
            replyData.UserId,
            selectedTranslation.LanguageToLearnId!.Value,
            topicIds,
            cefrLevelIds,
            ct);

        var topicNames = GetTopicNamesString(topics);
        
        var cefrLevelNames = cefrLevels.Length > 0
            ? string.Join(", ", cefrLevels.Select(x => x.Name))
            : $"{QuizMode.All} (A1-C1)";
        
        var questionsToAsk = dbQuestionsCount > QuestionsCount
            ? QuestionsCount
            : dbQuestionsCount;
        
        tmb
            .AppendRow($"<b>{QuizMode.QuizReady}</b>")
            .AppendRow()
            .AppendRow($"{QuizMode.LanguagePair}: <b>English → {languageName}</b>")
            .AppendRow($"{QuizMode.Topics}: <b>{topicNames}</b>")
            .AppendRow($"{QuizMode.CefrLevel}: <b>{cefrLevelNames}</b>")
            .AppendRow($"{QuizMode.QuestionsWillBeAsked}: <b>{questionsToAsk}</b>")
            .AppendRow()
            .AppendRow($"{QuizMode.TotalQuestionsByCriteria}")
            .AppendRow($"{QuizMode.CorrectAnswers}: <b>{learnStat.TotalAnswersCorrect}</b>")
            .AppendRow($"{QuizMode.IncorrectAnswers}: <b>{learnStat.TotalAnswersIncorrect}</b>")
            .AppendRow($"{QuizMode.SkippedAnswers}: <b>{learnStat.TotalAnswersSkipped}</b>")
            .Append($"{QuizMode.Learned}: <b>{learnStat.Learned} / {learnStat.Total}</b> {QuizMode.Meanings}");
            
        tmb
            .AddInlineKeyboardButtons([InlineKeyboardButton.WithCallbackData(
                QuizMode.StartButtonName,
                new CallbackRoutePath(TelegramRoutes.StartQuiz, RouteMethod.Post)
                    .WithTranslationDirection(selectedTranslation))])
            .AddInlineKeyboardButtons([
                InlineKeyboardButton.WithCallbackData(
                QuizMode.TopicsWithIcon,
                new CallbackRoutePath(TelegramRoutes.TopicSelection)
                    .WithTranslationDirection(selectedTranslation)),
                InlineKeyboardButton.WithCallbackData(
                    QuizMode.CefrLevelWithIcon,
                    new CallbackRoutePath(TelegramRoutes.CefrLevelSelection)
                        .WithTranslationDirection(selectedTranslation))
            ]);
        
        // Add back button only when language pair setup is available
        var isDefaultLanguagePairSet = await repository.DoesUserSetDefaultLanguagePairAsync(replyData.UserId, ct);
        if (!isDefaultLanguagePairSet)
        {
            tmb.AddInlineKeyboardButtons([
                InlineKeyboardButton.WithCallbackData(
                    QuizMode.ChangeLanguagePair,
                    TelegramRoutes.CurrentQuiz)
            ]);
        }
        
        tmb.AddBackMenuButton(TelegramRoutes.Menu);
        
        await client.EditMessageTextAsync(replyData, tmb, ParseMode.Html, cancellationToken: ct);
    }

    private async Task OpenNextQuizQuestionWindowAsync(
        ReplyData replyData,
        HandleSelectedOptionResponse? previousAnswerResult,
        CancellationToken ct = default)
    {
        var tmb = new TelegramMessageBuilder();

        if (previousAnswerResult != null)
        {
            tmb
                .Append(QuizMode.ResourceManager.GetString($"QuizAnswer_{previousAnswerResult.Status}") ?? string.Empty)
                .Append(" ")
                .Append(previousAnswerResult.QuestionDto.Word)
                .Append(" — ")
                .AppendRow(previousAnswerResult.QuestionDto.Translation);

            if (previousAnswerResult.Status != UserQuizQuestionStatus.Correct)
            {
                tmb
                    .Append("[")
                    .Append(previousAnswerResult.QuestionDto.Transcription!)
                    .Append("] ")
                    .Append(previousAnswerResult.QuestionDto.PartOfSpeech)
                    .Append(" · ")
                    .AppendRow(previousAnswerResult.QuestionDto.CefrLevel);
            }
            
            tmb
                .AppendRow()
                .AppendRow(CommonStrings.Divider);
        }
        
        
        var stats = await repository.GetCurrentQuizStatsAsync(replyData.UserId, ct);
        if (stats.AnsweredQuestions == stats.TotalQuestions)
        {
            await HandleFinishQuizAsync(replyData, stats.Id, stats.LanguageId, ct);
            return;
        }
        
        var data = await repository.GetFlashCardsAsync(stats.Id, stats.LanguageId, ct);

        tmb
            .AppendRow($"{QuizMode.Question} {stats.AnsweredQuestions + 1}/{stats.TotalQuestions}")
            .AppendRow()
            .AppendRow($"<b>{data.Word}</b>")
            .Append(data.PartOfSpeech);
        
        if (!string.IsNullOrEmpty(data.CefrLevel))
            tmb.Append($" · {data.CefrLevel}");

        foreach (var flashCardsChunk in data.FlashCards.Chunk(2))
        {
            tmb.AddInlineKeyboardButtons(flashCardsChunk
                .Select(x => InlineKeyboardButton.WithCallbackData(
                    System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(x.Text),
                    new CallbackRoutePath(TelegramRoutes.SelectQuizAnswer, RouteMethod.Post)
                        .WithQueryParameter(ParameterNames.QuestionId, data.QuestionId)
                        .WithQueryParameter(ParameterNames.AnswerWordId, x.WordId))));
        }

        tmb.AddInlineKeyboardButtons([
            InlineKeyboardButton.WithCallbackData(
                QuizMode.SkipButtonName,
                new CallbackRoutePath(TelegramRoutes.SelectQuizAnswer, RouteMethod.Post)
                    .WithQueryParameter(ParameterNames.QuestionId, data.QuestionId)
                    .WithQueryParameter(ParameterNames.AnswerWordId, NullOptionId))
        ]);
        
        tmb.AddInlineKeyboardButtons([
            InlineKeyboardButton.WithCallbackData(
                QuizMode.FinishQuiz,
                new CallbackRoutePath(TelegramRoutes.FinishQuiz, RouteMethod.Post))
        ]);
        
        tmb.AddBackMenuButton(TelegramRoutes.Menu);

        await client.EditMessageTextAsync(replyData, tmb, ParseMode.Html, cancellationToken: ct);
    }

    private async Task HandleFinishQuizAsync(
        ReplyData replyData,
        long quizId,
        long languageId,
        CancellationToken ct = default)
    {
        await repository.SetQuizFinished(quizId, ct);
        metrics.RecordQuizFinished();
        var learnStat = await repository.GetLearnStatAsync(
            replyData.UserId,
            languageId,
            [],
            [],
            ct);
        
        var lastQuizQuestions = await repository.GetLastQuizQuestionsAsync(replyData.UserId, ct);
        var correctCount = lastQuizQuestions.Count(q => q.Status == UserQuizQuestionStatus.Correct);
        var skippedCount = lastQuizQuestions.Count(q => q.Status == UserQuizQuestionStatus.Skipped);
        var incorrectCount = lastQuizQuestions.Length - correctCount - skippedCount;

        var tmb = new TelegramMessageBuilder()
            .AppendRow(QuizMode.QuizFinishedTitle)
            .AppendRow();

        for (var index = 0; index < lastQuizQuestions.Length; index++)
        {
            var lastQuizQuestion = lastQuizQuestions[index];
            
            tmb
                .Append(QuizMode.ResourceManager.GetString($"QuizAnswer_{lastQuizQuestion.Status}") ?? string.Empty)
                .Append(" ")
                .Append(lastQuizQuestion.Word)
                .Append(" - ")
                .Append(lastQuizQuestion.Translation);

            tmb.AppendRow();
        }

        tmb.AppendRow();

        var learnedInSessionCount = lastQuizQuestions
            .Where(q => q.Status == UserQuizQuestionStatus.Correct)
            .Count(q => q.LearnedAttempts == WinStreakToLearn);
        
        
        tmb
            .AppendRow(CommonStrings.Divider)
            .AppendRow(QuizMode.ThisSession);
            
        if (correctCount == lastQuizQuestions.Length)
            tmb.AppendRow($"{QuizMode.Perfect}");
        
        tmb
            .AppendRow($"{QuizMode.CorrectAnswers}: {correctCount}")
            .AppendRow($"{QuizMode.IncorrectAnswers}: {incorrectCount}")
            .AppendRow($"{QuizMode.SkippedAnswers}: {skippedCount}");

        if (learnedInSessionCount > 0)
            tmb.AppendRow($"{QuizMode.Learned}: +{learnedInSessionCount} {QuizMode.Meanings}");
            
        tmb
            .AppendRow(CommonStrings.Divider)
            .AppendRow(QuizMode.OverallProgress)
            .AppendRow($"{QuizMode.CorrectAnswers}: {learnStat.TotalAnswersCorrect}")
            .AppendRow($"{QuizMode.IncorrectAnswers}: {learnStat.TotalAnswersIncorrect}")
            .AppendRow($"{QuizMode.SkippedAnswers}: {learnStat.TotalAnswersSkipped}");

        if (learnStat.Learned > 0)
            tmb.Append($"{QuizMode.Learned}: {learnStat.Learned} / {learnStat.Total} {QuizMode.Meanings}");
            
        tmb
            .AddInlineKeyboardButtons([
                InlineKeyboardButton.WithCallbackData(
                    Buttons.RepeatQuiz,
                    new CallbackRoutePath(TelegramRoutes.StartQuiz, RouteMethod.Post)
                        .WithTranslationDirection(new SelectedTranslation(languageId)))
            ])
            .AddMainMenuButton();
        
        await client.EditMessageTextAsync(replyData, tmb, ParseMode.Html, cancellationToken: ct);
    }

    /// <returns>True if request handled and processing should be stopped.</returns>
    private async Task<HandleSelectedOptionResponse?> HandleSelectedOptionAsync(
        Guid userId,
        long questionId,
        long selectedOptionId,
        CancellationToken ct = default)
    {
        var currentQuestion = await repository.GetQuestion(userId, ct);
        if (currentQuestion.QuestionId != questionId)
            // Race condition, the answer is already processed
            return null;

        var status = selectedOptionId == NullOptionId
            ? UserQuizQuestionStatus.Skipped
            : selectedOptionId == currentQuestion.CorrectWordId
                ? UserQuizQuestionStatus.Correct
                : UserQuizQuestionStatus.Incorrect;
        
        await repository.SetQuizQuestionStatus(currentQuestion.QuestionId, status, ct);
        metrics.RecordQuizAnswer(status);

        var increaseWinStreak = status == UserQuizQuestionStatus.Correct;
        await repository.UpdateTranslationWinStreakAsync(
            currentQuestion.CorrectWordId,
            currentQuestion.LanguageId,
            userId,
            increaseWinStreak,
            ct);

        return new HandleSelectedOptionResponse
        {
            Status = status,
            QuestionDto = currentQuestion
        };
    }

    public class HandleSelectedOptionResponse
    {
        public required UserQuizQuestionStatus Status { get; set; }
        public required QuestionDto QuestionDto { get; set; }
    }

    public interface IRepository
    {
        Task<bool> HasActiveQuizAsync(Guid userId, CancellationToken ct = default);
        
        Task<long> CreateQuizAsync(
            Guid userId,
            long languageId,
            CancellationToken ct = default);
        
        Task SetQuizFinished(long quizId, CancellationToken ct = default);
        Task SaveQuizQuestionsAsync(long quizId, NewQuestionDto[] questions, CancellationToken ct = default);
        Task SkipAllQuizQuestions(Guid userId, CancellationToken ct = default);
        Task<FlashCardsDto> GetFlashCardsAsync(long quizId, long languageId, CancellationToken ct = default);
        Task<QuestionDto> GetQuestion(Guid userId, CancellationToken ct = default);
        Task SetQuizQuestionStatus(long questionId, UserQuizQuestionStatus status, CancellationToken ct = default);
        Task UpdateTranslationWinStreakAsync(long wordId, long languageId, Guid userId, bool increase, CancellationToken ct = default);
        Task<CurrentQuizStats> GetCurrentQuizStatsAsync(Guid userId, CancellationToken ct = default);
        Task<LastQuizStatsQuestion[]> GetLastQuizQuestionsAsync(Guid userId, CancellationToken ct = default);
        
        Task<LearnStat> GetLearnStatAsync(
            Guid userId,
            long languageId,
            long[] topicIds,
            long[] cefrLevelIds,
            CancellationToken ct = default);
        
        Task<string?> GetLanguageNameAsync(long languageId, CancellationToken ct = default);
        Task<TopicItemDto[]> GetUserQuizTopicsAsync(
            Guid userId,
            CancellationToken ct = default);
        
        Task<CefrLevelItemDto[]> GetUserCefrLevelsAsync(
            Guid userId,
            CancellationToken ct = default);
        
        Task<int> GetQuestionsCountByFilterAsync(
            long languageId,
            long[] topicIds,
            long[] cefrLevelIds,
            CancellationToken ct = default);
        
        Task<UserTopicItemDto[]> GetTopicsAsync(
            int count,
            Guid userId,
            long[] cefrLevelIds,
            CancellationToken ct = default);
        
        Task<UserCefrLevelItemDto[]> GetUserSelectedCefrLevelsAsync(
            Guid userId,
            long[] topicIds,
            CancellationToken ct = default);
        
        Task UpdateTopicAsync(
            Guid userId,
            long topicId,
            bool enable,
            CancellationToken ct = default);
        
        Task UpdateCefrLevelAsync(
            Guid userId,
            long cefrLevelId,
            bool enable,
            CancellationToken ct = default);
        
        Task<bool> DoesUserSetDefaultLanguagePairAsync(Guid userId, CancellationToken ct = default);
    }

    public class FlashCard
    {
        public required long WordId { get; set; }
        public required string Text { get; set; }
    }

    public class CurrentQuizStats
    {
        public long Id { get; set; }
        public long LanguageId { get; set; }
        public int TotalQuestions { get; set; }
        public int AnsweredQuestions { get; set; }
    }

    public class LastQuizStatsQuestion
    {
        public required string Word { get; init; }
        public required string Translation { get; init; }
        public required string? Transcription { get; init; }
        public required UserQuizQuestionStatus Status { get; init; }
        public required int LearnedAttempts { get; init; }
    }
    
    public class LearnStat
    {
        public required int Learned { get; init; }
        public required int Total { get; init; }
        public required int TotalAnswersSkipped { get; init; }
        public required int TotalAnswersCorrect { get; init; }
        public required int TotalAnswersIncorrect { get; init; }
    }
    
    public class FlashCardsDto
    {
        public required string Word { get; set; }
        public required string PartOfSpeech { get; set; }
        public required string? CefrLevel { get; set; }
        public required FlashCard[] FlashCards { get; set; }
        public required long QuestionId { get; set; }
    }

    public class QuestionDto
    {
        public long CorrectWordId { get; init; }
        public long QuestionId { get; init; }
        public required string Word { get; init; }
        public required string Translation { get; init; }
        public required string? Transcription { get; init; }
        public required long LanguageId { get; init; }
        public required string PartOfSpeech { get; init; }
        public required string CefrLevel { get; init; }
    }

    public class TopicItemDto
    {
        public long Id { get; init; }
        public int WordsCount { get; init; }
        public required string Name { get; init; }
    }
    
    public class UserTopicItemDto : TopicItemDto
    {
        public required bool IsSelected { get; init; }
    }

    public class CefrLevelItemDto
    {
        public long Id { get; init; }
        public int WordsCount { get; init; }
        public required string Name { get; init; }
    }

    public class UserCefrLevelItemDto : CefrLevelItemDto
    {
        public required bool IsSelected { get; init; }
    }
    
    public class Repository(DatabaseContext context, IDateTimeProvider dateTimeProvider) : IRepository
    {
        public Task<bool> HasActiveQuizAsync(Guid userId, CancellationToken ct = default)
        {
            return context.UserQuizzes
                .Where(x => x.UserId == userId)
                .Where(x => x.Status == UserQuizStatus.Active)
                .AnyAsyncLinqToDB(ct);
        }

        public async Task<long> CreateQuizAsync(
            Guid userId,
            long languageId,
            CancellationToken ct = default)
        {
            var quiz = new UserQuiz
            {
                UserId = userId,
                Status = UserQuizStatus.Active,
                CreatedAt = dateTimeProvider.UtcNow,
                LanguageId = languageId,
            };
            
            context.Add(quiz);
            
            await context.SaveChangesAsync(ct);
            
            return quiz.Id;
        }

        public Task SetQuizFinished(long quizId, CancellationToken ct = default)
        {
            return context.UserQuizzes
                .Where(q => q.Id == quizId)
                .ExecuteUpdateAsync(update => update
                        .SetProperty(x => x.Status, UserQuizStatus.Finished)
                        .SetProperty(x => x.FinishedAt, dateTimeProvider.UtcNow),
                    ct);
        }

        public async Task SaveQuizQuestionsAsync(
            long quizId,
            NewQuestionDto[] questions,
            CancellationToken ct = default)
        {
            foreach (var question in questions)
            {
                var entity = new UserQuizQuestion
                {
                    WordId = question.WordId,
                    Status = UserQuizQuestionStatus.New,
                    QuizId = quizId,
                    OptionIds = question.OptionIds,
                };
                
                context.UserQuizQuestions.Add(entity);
            }

            await context.SaveChangesAsync(ct);
        }

        public Task SkipAllQuizQuestions(Guid userId, CancellationToken ct = default)
        {
            return context.UserQuizQuestions
                .Where(x => x.Quiz.UserId == userId)
                .Where(x => x.Quiz.Status == UserQuizStatus.Active)
                .Where(x => x.Status == UserQuizQuestionStatus.New)
                .UpdateAsync(question => new UserQuizQuestion
                {
                    Status = UserQuizQuestionStatus.Skipped,
                    AnsweredAt = dateTimeProvider.UtcNow,
                }, ct);
        }

        public async Task<FlashCardsDto> GetFlashCardsAsync(long quizId, long languageId, CancellationToken ct = default)
        {
            var nextQuizQuestion = await context.UserQuizQuestions
                .Where(x => x.QuizId == quizId)
                .Where(x => x.Status == UserQuizQuestionStatus.New)
                .OrderBy(x => x.Id)
                .Select(x => new
                {
                    x.Id,
                    x.OptionIds,
                    x.Word.Text,
                    PartOfSpeech = x.Word.PartOfSpeech!.Name,
                    CefrLevel = x.Word.CefrLevel!.Name,
                })
                .FirstOrThrowNotFoundEFAsync($"Active quiz: {quizId} is not found", ct);

            var flashCards = await context.Translations
                .Where(x => x.LanguageId == languageId)
                .Where(x => nextQuizQuestion.OptionIds.Contains(x.WordId))
                .Select(x => new FlashCard
                {
                    WordId = x.WordId,
                    Text = x.Text,
                })
                .OrderBy(x => x.Text)
                .ToArrayAsyncEF(ct);

            return new FlashCardsDto
            {
                Word = nextQuizQuestion.Text,
                FlashCards = flashCards,
                PartOfSpeech = nextQuizQuestion.PartOfSpeech,
                CefrLevel = nextQuizQuestion.CefrLevel,
                QuestionId = nextQuizQuestion.Id,
            };
        }

        public Task<QuestionDto> GetQuestion(Guid userId, CancellationToken ct = default)
        {
            return context.UserQuizQuestions
                .Where(x => x.Quiz.UserId == userId)
                .Where(x => x.Quiz.Status == UserQuizStatus.Active)
                .Where(x => x.Status == UserQuizQuestionStatus.New)
                .OrderBy(x => x.Id)
                .Select(x => new QuestionDto
                {
                    Word = x.Word.Text,
                    CorrectWordId = x.WordId,
                    QuestionId = x.Id,
                    LanguageId = x.Quiz.LanguageId,
                    Translation = context.Translations
                        .Where(y => y.LanguageId == x.Quiz.LanguageId)
                        .First(y => y.WordId == x.WordId)
                        .Text,
                    Transcription = x.Word.Transcription,
                    PartOfSpeech = x.Word.PartOfSpeech!.Name,
                    CefrLevel = x.Word.CefrLevel!.Name,
                })
                .FirstOrThrowNotFoundEFAsync("Active question is not found", ct);
        }

        public Task SetQuizQuestionStatus(long questionId, UserQuizQuestionStatus status, CancellationToken ct = default)
        {
            return context.UserQuizQuestions
                .Where(x => x.Id == questionId)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(y => y.Status, status)
                    .SetProperty(y => y.AnsweredAt, dateTimeProvider.UtcNow),
                     ct);
        }

        public Task UpdateTranslationWinStreakAsync(long wordId, long languageId,  Guid userId, bool increase, CancellationToken ct = default)
        {
            return context.LearnedTranslations
                .Merge()
                .Using([new LearnedTranslation { WordId = wordId, LanguageId = languageId, UserId = userId}])
                .On(
                    x => new { x.WordId, x.LanguageId, x.UserId },
                    x => new { x.WordId, x.LanguageId, x.UserId })
                .InsertWhenNotMatched(x => new LearnedTranslation
                {
                    LanguageId = x.LanguageId,
                    WordId = x.WordId,
                    UserId = x.UserId,
                    WinStreakCount = increase ? 1 : 0,
                    LearnedAt =  increase ? x.WinStreakCount + 1 >= WinStreakToLearn ? dateTimeProvider.UtcNow : null : null,
                })
                .UpdateWhenMatched((o, n) => new LearnedTranslation
                {
                    WinStreakCount = increase ? o.WinStreakCount + 1 : 0,
                    LearnedAt = increase ? o.WinStreakCount + 1 >= WinStreakToLearn ? dateTimeProvider.UtcNow : null : null,
                })
                .MergeAsync(ct);
        }

        public Task<CurrentQuizStats> GetCurrentQuizStatsAsync(Guid userId, CancellationToken ct = default)
        {
            return context.UserQuizzes
                .Where(q => q.UserId == userId)
                .Where(q => q.Status == UserQuizStatus.Active)
                .Select(x => new CurrentQuizStats
                {
                    TotalQuestions = x.UserQuizQuestions.Count,
                    AnsweredQuestions = x.UserQuizQuestions.Count(y => y.Status != UserQuizQuestionStatus.New),
                    Id = x.Id,
                    LanguageId = x.LanguageId,
                })
                .FirstOrThrowNotFoundEFAsync("Active quiz is not found", ct);
        }

        public async Task<LastQuizStatsQuestion[]> GetLastQuizQuestionsAsync(Guid userId, CancellationToken ct = default)
        {
            var quiz = await context.UserQuizzes
                .Where(q => q.UserId == userId)
                .Where(q => q.Status != UserQuizStatus.Active)
                .OrderByDescending(x => x.FinishedAt)
                .Select(x => new { x.Id })
                .FirstOrDefaultAsyncEF(ct);

            if (quiz == null)
            {
                return [];
            }
            
            return await context.UserQuizQuestions
                .Where(x => x.QuizId == quiz.Id)
                .OrderBy(x => x.Id)
                .Select(x => new LastQuizStatsQuestion
                {
                    Status = x.Status,
                    Word = x.Word.Text,
                    Translation = context.Translations
                        .Where(y => y.LanguageId == x.Quiz.LanguageId)
                        .First(y => y.WordId == x.WordId)
                        .Text,
                    Transcription = x.Word.Transcription,
                    LearnedAttempts = context.LearnedTranslations
                        .Where(t => t.LanguageId == x.Quiz.LanguageId)
                        .Where(t => t.UserId == x.Quiz.UserId)
                        .Where(t => t.WordId == x.WordId)
                        .Select(t => t.WinStreakCount)
                        .FirstOrDefault()
                })
                .ToArrayAsyncEF(ct);
        }

        public async Task<LearnStat> GetLearnStatAsync(
            Guid userId,
            long languageId,
            long[] topicIds,
            long[] cefrLevelIds,
            CancellationToken ct = default)
        {
            var query = context.LearnedTranslations
                .Where(q => q.UserId == userId)
                .Where(q => q.LanguageId == languageId);
            
            if (topicIds.Length > 0)
                query = query.Where(tr => tr.Word.Topics
                    .Any(t => topicIds.Any(topicId => t.TopicId == topicId)));
            
            if (cefrLevelIds.Length > 0)
                query = query
                    .Where(tr => cefrLevelIds
                        .Any(cefrLevelId => tr.Word.CefrLevelId == cefrLevelId));
            
            var learnedCount = await query
                .CountAsyncEF(x => x.LearnedAt != null, ct);
            
            var totalCountQuery = context.Translations
                .Where(x => x.LanguageId == languageId);
            
            if (topicIds.Length > 0)
                totalCountQuery = totalCountQuery
                    .Where(tr => tr.Word.Topics
                        .Any(t => topicIds.Any(topicId => t.TopicId == topicId)));
            
            if (cefrLevelIds.Length > 0)
                totalCountQuery = totalCountQuery
                    .Where(tr => cefrLevelIds
                        .Any(cefrLevelId => tr.Word.CefrLevelId == cefrLevelId));

            var answersStatQuery = context.UserQuizQuestions
                .Where(q => q.Quiz.UserId == userId)
                .Where(q => q.Status != UserQuizQuestionStatus.New);
            
            if (topicIds.Length > 0)
                answersStatQuery = answersStatQuery
                    .Where(q => q.Word.Topics
                        .Any(t => topicIds.Any(topicId => t.TopicId == topicId)));
            
            if (cefrLevelIds.Length > 0)
                answersStatQuery = answersStatQuery
                    .Where(q => cefrLevelIds
                        .Any(cefrLevelId => q.Word.CefrLevelId == cefrLevelId));

            var answersStat = await answersStatQuery
                .GroupBy(q => q.Status)
                .Select(q => new
                {
                    q.Key,
                    Count = q.Count(),
                })
                .ToDictionaryAsyncEF(x => x.Key, x => x.Count, ct);
            
            var totalCount = await totalCountQuery.CountAsyncEF(ct);

            return new LearnStat
            {
                Learned = learnedCount,
                Total = totalCount,
                TotalAnswersCorrect = answersStat
                    .GetValueOrDefault(UserQuizQuestionStatus.Correct),
                TotalAnswersIncorrect = answersStat
                    .GetValueOrDefault(UserQuizQuestionStatus.Incorrect),
                TotalAnswersSkipped = answersStat
                    .GetValueOrDefault(UserQuizQuestionStatus.Skipped),
            };
        }

        public Task<string?> GetLanguageNameAsync(long languageId, CancellationToken ct = default)
        {
            return context.Languages
                .Where(x => x.Id == languageId)
                .Select(x => x.Description)
                .FirstOrDefaultAsyncEF(ct);
        }

        public Task<TopicItemDto[]> GetUserQuizTopicsAsync(Guid userId, CancellationToken ct = default)
        {
            return context.UserQuizTopics
                .Where(u => u.UserId == userId)
                .OrderBy(u => u.Topic.Name)
                .Select(u => new TopicItemDto
                {
                    Name = u.Topic.Name,
                    Id = u.Topic.Id,
                    WordsCount = u.Topic.WordTopics.Count
                })
                .ToArrayAsyncEF(ct);
        }

        public Task<CefrLevelItemDto[]> GetUserCefrLevelsAsync(
            Guid userId,
            CancellationToken ct = default)
        {
            return context.UserQuizCefrLevels
                .Where(u => u.UserId == userId)
                .OrderBy(u => u.CefrLevelId)
                .Select(u => new CefrLevelItemDto
                {
                    Name = u.CefrLevel.Name,
                    Id = u.CefrLevel.Id,
                    WordsCount = u.CefrLevel.Words.Count,
                })
                .ToArrayAsyncEF(ct);
        }

        public Task<int> GetQuestionsCountByFilterAsync(
            long languageId,
            long[] topicIds,
            long[] cefrLevelIds,
            CancellationToken ct = default)
        {
            var query = context.Translations
                .Where(x => x.LanguageId == languageId);

            if (topicIds.Length > 0)
                query = query.Where(x => x.Word.Topics
                    .Any(t => topicIds
                        .Any(topicId => t.TopicId == topicId)));
            
            if (cefrLevelIds.Length != 0)
                query = query.Where(x => cefrLevelIds
                    .Any(cefrLevelId => x.Word.CefrLevelId == cefrLevelId));
            
            return query.CountAsyncEF(ct);
        }

        public async Task<UserTopicItemDto[]> GetTopicsAsync(
            int count,
            Guid userId,
            long[] cefrLevelIds,
            CancellationToken ct = default)
        {
            var query = context.Topics.AsQueryable();
            
            var topics = await query
                .Select(topic => new UserTopicItemDto
                {
                    Id = topic.Id,
                    WordsCount = topic.WordTopics
                        .Count(
                            wt => cefrLevelIds.Length == 0 || cefrLevelIds
                                .Any(cefrLevelId => wt.Word.CefrLevelId == cefrLevelId)),
                    Name = topic.Name,
                    IsSelected = topic.UserQuizTopics
                        .Any(q => q.UserId == userId),
                })
                .OrderByDescending(x => x.WordsCount)
                .Take(count)
                .ToArrayAsyncEF(ct);

            return topics
                .OrderBy(x => x.Name)
                .ToArray();
        }

        public Task<UserCefrLevelItemDto[]> GetUserSelectedCefrLevelsAsync(
            Guid userId,
            long[] topicIds,
            CancellationToken ct = default)
        {
            var query = context.CefrLevels.AsQueryable();
            
            return query
                .Select(cefrLevel => new UserCefrLevelItemDto
                {
                    Id = cefrLevel.Id,
                    WordsCount = cefrLevel.Words
                        .Count(w => topicIds.Length == 0 || w.Topics
                            .Any(t => topicIds
                                .Any(topicId => t.TopicId == topicId))),
                    Name = cefrLevel.Name,
                    IsSelected = cefrLevel.UserQuizCefrLevels
                        .Any(q => q.UserId == userId),
                })
                .OrderBy(x => x.Id)
                .Where(x => x.WordsCount > 0)
                .ToArrayAsyncEF(ct);
        }

        public Task UpdateTopicAsync(
            Guid userId,
            long topicId,
            bool enable,
            CancellationToken ct = default)
        {
            if (enable)
            {
                return context.UserQuizTopics
                    .Merge()
                    .Using([new UserQuizTopic { TopicId = topicId, UserId = userId }])
                    .OnTargetKey()
                    .InsertWhenNotMatched()
                    .MergeAsync(ct);
            }

            return context.UserQuizTopics
                .Where(x => x.UserId == userId && x.TopicId == topicId)
                .ExecuteDeleteAsync(ct);
        }

        public Task UpdateCefrLevelAsync(
            Guid userId,
            long cefrLevelId,
            bool enable,
            CancellationToken ct = default)
        {
            if (enable)
            {
                return context.UserQuizCefrLevels
                    .Merge()
                    .Using([new UserQuizCefrLevel { CefrLevelId = cefrLevelId, UserId = userId }])
                    .OnTargetKey()
                    .InsertWhenNotMatched()
                    .MergeAsync(ct);
            }

            return context.UserQuizCefrLevels
                .Where(x => x.UserId == userId && x.CefrLevelId == cefrLevelId)
                .ExecuteDeleteAsync(ct);
        }

        public Task<bool> DoesUserSetDefaultLanguagePairAsync(Guid userId, CancellationToken ct = default)
        {
            return context.Users
                .Where(u => u.Id == userId)
                .AnyAsyncEF(u => u.LanguageToLearnId != null, ct);
        }
    }
}