using Laraue.Apps.LearnLanguage.DataAccess;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.LearnLanguage.AppServices.Services;

public interface IQuestionsGenerator
{
    Task<NewQuestionDto[]> GenerateQuestions(
        Guid userId,
        long languageId,
        long[] topicIds,
        long[] cefrLevelIds,
        int questionsCount,
        int optionsCount,
        CancellationToken ct);
}

public class QuestionsGenerator(
    DatabaseContext context,
    IRandomizer randomizer)
    : IQuestionsGenerator
{
    public async Task<NewQuestionDto[]> GenerateQuestions(
        Guid userId,
        long languageId,
        long[] topicIds,
        long[] cefrLevelIds,
        int questionsCount,
        int optionsCount,
        CancellationToken ct)
    {
        const double rememberWordsRatio = 0.2;
        const double repeatWordsRatio = 0.6;

        var preferredRememberWordsCount = (int)(questionsCount * rememberWordsRatio);
        var preferredRepeatWordsCount = (int)(questionsCount * repeatWordsRatio);

        var metWordsQuery = context.LearnedTranslations
            .Where(x => x.UserId == userId)
            .Where(x => x.Translation.LanguageId == languageId);

        if (topicIds.Length > 0)
            metWordsQuery = metWordsQuery
                .Where(x => x.Word.Topics
                    .Any(t => topicIds
                        .Any(topicId => t.TopicId == topicId)));
        
        if (cefrLevelIds.Length != 0)
            metWordsQuery = metWordsQuery
                .Where(x => cefrLevelIds
                    .Any(cefrLevelId => x.Word.CefrLevelId == cefrLevelId));

        var oldQuestions = await randomizer.InRandomOrder(metWordsQuery
            .Where(x => x.LearnedAt != null)
            .Select(x => new NewQuestionDto
            {
                WordId = x.Translation.WordId,
                PartOfSpeechId = x.Translation.Word.PartOfSpeechId,
                TranslationHashCode = x.Translation.Text.GetHashCode(),
            }))
            .Take(preferredRememberWordsCount)
            .ToListAsyncEF(ct);
        
        // If remember words are less than excepted, request more words to repeat
        var repeatWordsCount = preferredRepeatWordsCount + preferredRememberWordsCount - oldQuestions.Count;
        
        var repeatQuestions = await randomizer.InRandomOrder(metWordsQuery
            .Where(x => x.LearnedAt == null)
            .Select(x => new NewQuestionDto
            {
                WordId = x.Translation.WordId,
                PartOfSpeechId = x.Translation.Word.PartOfSpeechId,
                TranslationHashCode = x.Translation.Text.GetHashCode(),
            }))
            .Take(repeatWordsCount)
            .ToListAsyncEF(ct);
        
        var newQuestionsCount = questionsCount - oldQuestions.Count - repeatQuestions.Count;
        // New for this user: no learned translation of theirs, whatever other users have met.
        var newQuestionsQuery = context.Translations
            .LeftJoin(
                context.LearnedTranslations,
                (translation, learnedTranslation) => 
                    translation.LanguageId == learnedTranslation.LanguageId 
                    && translation.WordId == learnedTranslation.WordId
                    && learnedTranslation.UserId == userId,
                (translation, learnedTranslation) => new { translation, learnedTranslation })
            .Where(x => x.translation.LanguageId == languageId)
            .Where(x => x.learnedTranslation == null);

        if (topicIds.Length > 0)
            newQuestionsQuery = newQuestionsQuery
                .Where(q => q.translation.Word.Topics
                    .Any(t => topicIds
                        .Any(topicId => t.TopicId == topicId)));
        
        if (cefrLevelIds.Length != 0)
            newQuestionsQuery = newQuestionsQuery
                .Where(q => cefrLevelIds
                    .Any(cefrLevelId => q.translation.Word.CefrLevelId == cefrLevelId));
        
        var newQuestions = await randomizer.InRandomOrder(newQuestionsQuery
            .Select(x => new NewQuestionDto
            {
                WordId = x.translation.WordId,
                PartOfSpeechId = x.translation.Word.PartOfSpeechId,
                TranslationHashCode = x.translation.Text.GetHashCode(),
            }))
            .Take(newQuestionsCount)
            .ToListAsyncEF(ct);

        var allQuestions = randomizer.InRandomOrder(oldQuestions
            .Union(repeatQuestions)
            .Union(newQuestions))
            .ToArray();
        
        await EnrichOptions(languageId, optionsCount, allQuestions, ct);
        
        return allQuestions;
    }

    private async Task EnrichOptions(
        long languageId,
        int enrichCount,
        NewQuestionDto[] questions,
        CancellationToken ct)
    {
        var optionsData = await context.Translations
            .Where(x => x.LanguageId == languageId)
            .OrderBy(x => x.WordId)
            .Select(x => new
            {
                x.WordId,
                x.Word.PartOfSpeechId,
                HashCode = x.Text.GetHashCode()
            })
            .ToArrayAsyncEF(ct);

        var optionIdsByPartOfSpeechId = optionsData
            .GroupBy(x => x.PartOfSpeechId)
            .ToDictionary(
                x => x.Key,
                x => x
                    .Select(y => new { y.WordId, y.HashCode })
                    .ToArray());
        
        var allOptionIds = optionsData
            .Select(x => new { x.WordId, x.HashCode })
            .ToArray();
        
        foreach (var question in questions)
        {
            var generatedOptions = new HashSet<long>
            {
                question.WordId, // the first option is the correct value
            };

            var usedTranslationHashes = new HashSet<int>
            {
                question.TranslationHashCode,
            };
            
            // As default tries to generate options with the same part of speech
            var partOfSpeechId = question.PartOfSpeechId;
            var optionsLength = optionIdsByPartOfSpeechId[partOfSpeechId].Length;
            var availableOptionIds = enrichCount > optionsLength
                ? allOptionIds
                : optionIdsByPartOfSpeechId[partOfSpeechId];
            
            for (var i = 0; i < enrichCount - 1; i++)
            {
                while (true)
                {
                    var nextOptionIndex = randomizer.NextRandomValue(0, availableOptionIds.Length - 1);
                    var option = availableOptionIds[nextOptionIndex];

                    if (generatedOptions.Contains(option.WordId) || usedTranslationHashes.Contains(option.HashCode))
                        continue;
                    
                    generatedOptions.Add(option.WordId);
                    usedTranslationHashes.Add(option.HashCode);
                        
                    break;
                }
            }

            question.OptionIds = generatedOptions.ToArray();
        }
    }
}

public class NewQuestionDto
{
    public required long WordId { get; set; }
    public required long PartOfSpeechId { get; set; }
    public long[] OptionIds { get; set; } = [];
    public int TranslationHashCode { get; set; }
}