using Laraue.Apps.Identity.Internal.Contracts;
using Laraue.Apps.LearnLanguage.AppServices.Identity;
using Laraue.Apps.LearnLanguage.AppServices.Options;
using Laraue.Apps.LearnLanguage.AppServices.Services.Reminders;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;
using Laraue.Apps.LearnLanguage.Host;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using Xunit;
using User = Laraue.Apps.LearnLanguage.DataAccess.Entities.User;

namespace Laraue.Apps.LearnEnglish.IntegrationTests;

/// <summary>
/// Daily practice reminders and the one-time abandoned quiz message (MIN-30).
/// </summary>
[Collection("IntegrationTest")]
public class ReminderTests : IAsyncLifetime
{
    private static readonly DateTime Now = DateTime.UtcNow.Date.AddHours(10);
    private static readonly TimeOnly ReminderTime = new(9, 0);

    private readonly FakeBotClient _bot = new();
    private readonly RemindersOptions _options = new();
    private ServiceProvider _provider = null!;
    private long _ruLanguageId;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddJsonFile("appsettings.json");
        builder
            .AddTelegramOptions("Telegram")
            .AddIdentityServices()
            .AddApplicationServices()
            .AddDatabaseServices("Postgre");
        builder.Services
            .Replace(ServiceDescriptor.Singleton<UserIdentityService.UserIdentityServiceClient, FakeUserIdentityServiceClient>())
            .Replace(ServiceDescriptor.Singleton<ITelegramBotClient>(_bot))
            .Replace(ServiceDescriptor.Singleton(Microsoft.Extensions.Options.Options.Create(_options)));
        _provider = builder.Services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        await db.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<DictionarySync>().SyncAsync();
        await db.Users.ExecuteDeleteAsync();
        _ruLanguageId = await db.Languages.Where(l => l.Name == "ru").Select(l => l.Id).FirstAsync();
    }

    public async Task DisposeAsync()
    {
        using (var scope = _provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseContext>().Users.ExecuteDeleteAsync();
        }

        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task Daily_ShouldOfferReview_WhenUserDidNotPractise()
    {
        var user = await AddUserAsync(reminderTime: ReminderTime, lastQuizAt: Now.AddDays(-2), wordsInProgress: 3);

        await SendDueAsync();

        var message = Assert.Single(_bot.Sent);
        Assert.Equal(user.TelegramId, message.ChatId.Identifier);
        Assert.Equal("📚 Words waiting for review: 3. A quiz takes a couple of minutes.", message.Text.Trim());
        Assert.Equal<(string, string?)[][]>(
            [
                [("🎯 New Quiz", $"1 sq?lt={_ruLanguageId}")],
                [("🔕 Turn off reminders", "1 s/rem?e=false")],
            ],
            Buttons(message));
        Assert.Single(GetReminders(user.Id), r => r.Kind == ReminderKind.Daily);

        // At most one a day.
        await SendDueAsync(Now.AddHours(2));
        Assert.Single(_bot.Sent);
    }

    [Fact]
    public async Task Daily_ShouldOfferToContinue_WhenQuizIsUnfinished()
    {
        var user = await AddUserAsync(reminderTime: ReminderTime, lastQuizAt: Now.AddDays(-2));
        await AddActiveQuizAsync(user.Id, Now.AddDays(-2), questions: 6, answered: 1);

        await SendDueAsync();

        var message = Assert.Single(_bot.Sent);
        Assert.Equal("⏸ You stopped at question 2 of 6. Shall we continue?", message.Text.Trim());
        Assert.Equal("quiz", Buttons(message)[0][0].Item2);
    }

    [Fact]
    public async Task Daily_ShouldUseInterfaceLanguage()
    {
        await AddUserAsync(reminderTime: ReminderTime, lastQuizAt: Now.AddDays(-2), languageCode: "ru");

        await SendDueAsync();

        var message = Assert.Single(_bot.Sent);
        Assert.Equal("🎯 Пройдём короткий тест? Это займёт пару минут.", message.Text.Trim());
    }

    [Fact]
    public async Task Daily_ShouldNotSend_WhenNotDueOrPractisedRecently()
    {
        // Practised two hours ago.
        await AddUserAsync(reminderTime: ReminderTime, lastQuizAt: Now.AddHours(-2));
        // Reminder later in the day.
        await AddUserAsync(reminderTime: new TimeOnly(11, 0), lastQuizAt: Now.AddDays(-2));
        // Reminders off.
        await AddUserAsync(reminderTime: null, lastQuizAt: Now.AddDays(-2));

        await SendDueAsync();

        Assert.Empty(_bot.Sent);
    }

    [Fact]
    public async Task Daily_ShouldStopAfterIgnoredReminders_AndResumeAfterActivity()
    {
        var user = await AddUserAsync(reminderTime: ReminderTime, lastQuizAt: Now.AddDays(-5));
        for (var day = 1; day <= ReminderSender.MaxIgnoredInRow; day++)
        {
            await AddReminderAsync(user.Id, ReminderKind.Daily, Now.AddDays(-day));
        }

        await SendDueAsync();
        Assert.Empty(_bot.Sent);

        // The user came back yesterday, after those reminders.
        await AddActiveQuizAsync(user.Id, Now.AddDays(-1), questions: 6, answered: 0);

        await SendDueAsync();
        Assert.Single(_bot.Sent);
    }

    [Fact]
    public async Task Daily_ShouldStopForGood_WhenUserBlockedTheBot()
    {
        var user = await AddUserAsync(reminderTime: ReminderTime, lastQuizAt: Now.AddDays(-2));
        _bot.BlockedChatIds.Add(user.TelegramId);

        await SendDueAsync();

        Assert.Empty(GetReminders(user.Id));
        var blockedAt = GetFromDb(db => db.Users.Where(u => u.Id == user.Id).Select(u => u.BotBlockedAt).First());
        Assert.Equal(Now, blockedAt);

        await SendDueAsync(Now.AddDays(1));
        Assert.Equal(1, _bot.Attempts);
    }

    [Fact]
    public async Task AbandonedQuiz_ShouldBeSentOnce_WhenEnabled()
    {
        var user = await AddUserAsync(reminderTime: null, lastQuizAt: Now.AddDays(-3));
        await AddActiveQuizAsync(user.Id, Now.AddDays(-3), questions: 20, answered: 7);

        await SendDueAsync();
        Assert.Empty(_bot.Sent);

        _options.SendAbandonedQuizReminders = true;
        await SendDueAsync();

        var message = Assert.Single(_bot.Sent);
        Assert.Equal("⏸ You stopped at question 8 of 20. Shall we continue?", message.Text.Trim());
        Assert.Equal<(string, string?)[][]>(
            [
                [("▶️ Continue", "quiz")],
                [("🔔 Remind me daily at this time", "1 s/rem?e=true")],
            ],
            Buttons(message));

        await SendDueAsync(Now.AddDays(2));
        Assert.Single(_bot.Sent);
    }

    private async Task SendDueAsync(DateTime? now = null)
    {
        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReminderSender>().SendDueAsync(now ?? Now);
    }

    private async Task<User> AddUserAsync(
        TimeOnly? reminderTime,
        DateTime lastQuizAt,
        int wordsInProgress = 0,
        string? languageCode = null)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var user = new User
        {
            TelegramId = Random.Shared.NextInt64(1, long.MaxValue),
            GlobalUserId = Guid.NewGuid(),
            CreatedAt = lastQuizAt,
            ReminderTime = reminderTime,
            LanguageToLearnId = _ruLanguageId,
            TelegramLanguageCode = languageCode,
        };
        db.Users.Add(user);
        db.UserQuizzes.Add(new UserQuiz
        {
            User = user,
            LanguageId = _ruLanguageId,
            Status = UserQuizStatus.Finished,
            CreatedAt = lastQuizAt,
            FinishedAt = lastQuizAt,
        });

        var translations = await db.Translations
            .Where(t => t.LanguageId == _ruLanguageId)
            .OrderBy(t => t.WordId)
            .Take(wordsInProgress)
            .ToListAsync();
        db.LearnedTranslations.AddRange(translations.Select(t => new LearnedTranslation
        {
            User = user,
            WordId = t.WordId,
            LanguageId = t.LanguageId,
            WinStreakCount = 1,
        }));

        await db.SaveChangesAsync();
        return user;
    }

    private async Task AddActiveQuizAsync(Guid userId, DateTime createdAt, int questions, int answered)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var wordIds = await db.Words.OrderBy(w => w.Id).Select(w => w.Id).Take(questions).ToListAsync();

        db.UserQuizzes.Add(new UserQuiz
        {
            UserId = userId,
            LanguageId = _ruLanguageId,
            Status = UserQuizStatus.Active,
            CreatedAt = createdAt,
            UserQuizQuestions = wordIds
                .Select((wordId, i) => new UserQuizQuestion
                {
                    WordId = wordId,
                    Status = i < answered ? UserQuizQuestionStatus.Correct : UserQuizQuestionStatus.New,
                    AnsweredAt = i < answered ? createdAt : null,
                })
                .ToList(),
        });
        await db.SaveChangesAsync();
    }

    private async Task AddReminderAsync(Guid userId, ReminderKind kind, DateTime sentAt)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        db.Reminders.Add(new Reminder { UserId = userId, Kind = kind, SentAt = sentAt });
        await db.SaveChangesAsync();
    }

    private Reminder[] GetReminders(Guid userId) =>
        GetFromDb(db => db.Reminders.Where(r => r.UserId == userId).ToArray());

    private T GetFromDb<T>(Func<DatabaseContext, T> query)
    {
        using var scope = _provider.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<DatabaseContext>());
    }

    private static (string, string?)[][] Buttons(SendMessageRequest request) =>
        ((InlineKeyboardMarkup)request.ReplyMarkup!).InlineKeyboard
            .Select(row => row.Select(b => (b.Text, b.CallbackData)).ToArray())
            .ToArray();

    /// <summary>
    /// Records sent messages; chats in <see cref="BlockedChatIds"/> answer 403 like a user who blocked the bot.
    /// </summary>
    private sealed class FakeBotClient : ITelegramBotClient
    {
        public List<SendMessageRequest> Sent { get; } = [];

        public HashSet<long> BlockedChatIds { get; } = [];

        public int Attempts { get; private set; }

        public Task<TResponse> SendRequest<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is SendMessageRequest message)
            {
                Attempts++;
                if (BlockedChatIds.Contains(message.ChatId.Identifier!.Value))
                {
                    throw new ApiRequestException("Forbidden: bot was blocked by the user", 403);
                }

                Sent.Add(message);
                return Task.FromResult((TResponse)(object)new Message());
            }

            return Task.FromResult(default(TResponse)!);
        }

        public bool LocalBotServer => false;
        public long BotId => 1;
        public TimeSpan Timeout { get; set; }
        public IExceptionParser ExceptionsParser { get; set; } = null!;
        public event AsyncEventHandler<ApiRequestEventArgs>? OnMakingApiRequest { add { } remove { } }
        public event AsyncEventHandler<ApiResponseEventArgs>? OnApiResponseReceived { add { } remove { } }
        public Task<bool> TestApi(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task DownloadFile(string filePath, Stream destination, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DownloadFile(TGFile file, Stream destination, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
