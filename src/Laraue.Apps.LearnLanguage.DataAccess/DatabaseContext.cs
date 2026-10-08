using Laraue.Apps.LearnLanguage.DataAccess.Entities;
using Laraue.Apps.LearnLanguage.DataAccess.Extensions;
using Laraue.Telegram.NET.UpdatesQueue.EFCore;
using Laraue.Telegram.NET.UpdatesQueue.EFCore.DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.LearnLanguage.DataAccess;

public class DatabaseContext : DbContext, IUpdatesQueueDbContext
{
    public DatabaseContext(DbContextOptions options) 
        : base(options)
    {
    }
    
    public DbSet<WordLanguage> Languages { get; init; }
    
    public DbSet<Word> Words { get; init; }
    
    public DbSet<CefrLevel> CefrLevels { get; init; }
    
    public DbSet<UserQuizCefrLevel> UserQuizCefrLevels { get; init; }
    
    public DbSet<UserQuizTopic> UserQuizTopics { get; init; }
    
    public DbSet<Topic> Topics { get; init; }
    
    public DbSet<WordTopic> WordTopics { get; init; }
    
    public DbSet<Translation> Translations { get; init; }
    
    public DbSet<User> Users { get; init; }
    
    public DbSet<LearnedTranslation> LearnedTranslations { get; init; }

    public DbSet<UserQuiz> UserQuizzes { get; init; }

    public DbSet<UserQuizQuestion> UserQuizQuestions { get; init; }

    public DbSet<Update> Updates { get; set; }
    
    public DbSet<FailedUpdate> FailedUpdates { get; set; }
    
    public DbSet<UtmLabel> UtmLabels { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("uuid-ossp");

        modelBuilder.Entity<WordTopic>()
            .HasKey(x => new { x.WordId, x.TopicId });
        
        modelBuilder.Entity<Word>()
            .HasIndex(x => x.Text);

        modelBuilder.Entity<Translation>()
            .HasKey(x => new { x.WordId, x.LanguageId });

        modelBuilder.Entity<User>()
            .HasIndex(x => x.GlobalUserId)
            .IsUnique();

        modelBuilder.Entity<UserQuizCefrLevel>()
            .HasKey(x => new { x.UserId, x.CefrLevelId });

        modelBuilder.Entity<UserQuizTopic>()
            .HasKey(x => new { x.UserId, x.TopicId });
        
        modelBuilder.Entity<Translation>()
            .HasForeignKeyToWord(x => x.Translations);

        modelBuilder.Entity<LearnedTranslation>()
            .HasKey(x => new { x.WordId, x.LanguageId, x.UserId });
        
        modelBuilder.Entity<LearnedTranslation>()
            .HasForeignKeyToTranslation(x => x.LearnedTranslations)
            .HasForeignKeyToWord(x => x.LearnedTranslations);
        
        modelBuilder.Entity<LearnedTranslation>()
            .HasIndex(x => new { x.UserId, x.LearnedAt });
        
        modelBuilder.Entity<UserQuiz>()
            .HasIndex(x => x.UserId);
        
        modelBuilder.Entity<UserQuiz>()
            .HasIndex(x => x.LanguageId);
        
        modelBuilder.Entity<UserQuizQuestion>()
            .HasIndex(x => x.QuizId);
        
        modelBuilder.Entity<UserQuizQuestion>()
            .HasIndex(x => x.WordId);
        
        modelBuilder.Entity<UtmLabel>()
            .HasKey(x => new { x.UserId, x.Name });

        // Do not use memory on prod
        if (!IsMigrationRun())
            AddSeedData(modelBuilder);
    }
    
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(warnings =>
            warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

    private bool IsMigrationRun()
    {
        var designTimeServices = Database.GetInfrastructure().GetService<IDesignTimeServices>();
        return designTimeServices != null;
    }

    private void AddSeedData(ModelBuilder modelBuilder)
    {
        var partsOfSpeech = DefaultContextData.GetPartOfSpeeches();
        var wordTopics = DefaultContextData.GetWordTopics();
        var wordLanguages = DefaultContextData.GetWordLanguages();
        
        modelBuilder.Entity<WordLanguage>().HasData(wordLanguages.Items);
        modelBuilder.Entity<CefrLevel>().HasData(DefaultContextData.CefrLevels.Items);
        modelBuilder.Entity<Topic>().HasData(wordTopics.Items);
        modelBuilder.Entity<PartOfSpeech>().HasData(partsOfSpeech.Items);
        
        foreach (var word in DefaultContextData.GetWords())
        {
            modelBuilder.Entity<Word>()
                .HasData(new Word
                {
                    Id = word.Id,
                    Text = word.Word,
                    CefrLevelId = word.CefrLevel is not null ? DefaultContextData.CefrLevels.GetId(word.CefrLevel) : null,
                    Transcription = word.Transcription,
                    PartOfSpeechId = partsOfSpeech.GetId(word.PartOfSpeech),
                });
            
            foreach (var topic in word.Topics)
            {
                modelBuilder.Entity<WordTopic>()
                    .HasData(new WordTopic
                    {
                        WordId = word.Id,
                        TopicId = wordTopics.GetId(topic),
                    });
            }

            foreach (var translation in word.Translations)
            {
                modelBuilder.Entity<Translation>()
                    .HasData(new Translation
                    {
                        Text = translation.Text,
                        LanguageId = wordLanguages.GetId(translation.Language),
                        WordId = word.Id,
                        Transcription = translation.Transcription,
                    });
            }
        }
    }
}