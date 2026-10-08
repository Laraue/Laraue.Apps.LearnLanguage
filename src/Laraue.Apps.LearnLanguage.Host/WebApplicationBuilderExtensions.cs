using Laraue.Apps.Identity.Internal.Contracts;
using Laraue.Apps.LearnLanguage.AppServices;
using Laraue.Apps.LearnLanguage.AppServices.Identity;
using Laraue.Apps.LearnLanguage.AppServices.Metrics;
using Laraue.Apps.LearnLanguage.AppServices.Options;
using Laraue.Apps.LearnLanguage.AppServices.Repositories;
using Laraue.Apps.LearnLanguage.AppServices.Services;
using Laraue.Apps.LearnLanguage.AppServices.Services.LearnModes;
using Laraue.Apps.LearnLanguage.AppServices.Services.LearnModes.Group.CefrLevel;
using Laraue.Apps.LearnLanguage.AppServices.Services.LearnModes.Group.FirstLetter;
using Laraue.Apps.LearnLanguage.AppServices.Services.LearnModes.Group.Topic;
using Laraue.Apps.LearnLanguage.AppServices.Services.Quiz;
using Laraue.Apps.LearnLanguage.DataAccess;
using Laraue.Apps.LearnLanguage.DataAccess.Entities;
using Laraue.Core.DataAccess.Linq2DB.Extensions;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.DateTime.Services.Impl;
using Laraue.Telegram.NET.Authentication.Extensions;
using Laraue.Telegram.NET.Authentication.Services;
using Laraue.Telegram.NET.Core;
using Laraue.Telegram.NET.Core.Extensions;
using Laraue.Telegram.NET.Core.Middleware;
using Laraue.Telegram.NET.Localization;
using Laraue.Telegram.NET.Localization.Extensions;
using Laraue.Telegram.NET.UpdatesQueue.EFCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.LearnLanguage.Host;

public static class WebApplicationBuilderExtensions
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddTelegramOptions(string sectionName)
        {
            builder.Services.AddOptions<TelegramOptions>();
            builder.Services.Configure<TelegramOptions>(
                builder.Configuration.GetSection(sectionName));

            builder.Services.AddOptions<TelegramNetOptions>();
            builder.Services.Configure<TelegramNetOptions>(
                builder.Configuration.GetSection(sectionName));
            
            builder.Services.AddOptions<RoleUsers>();
            builder.Services.Configure<RoleUsers>(
                builder.Configuration.GetSection($"{sectionName}:UserNamesByRoles"));
            
            return builder;
        }
        
        public WebApplicationBuilder AddDatabaseServices(string connectionStringName)
        {
            var connection = GetConnection(builder, connectionStringName);
            
            builder.Services
                .AddDbContext<DatabaseContext>(opt =>
                {
                    opt.UseNpgsql(connection)
                        .UseSnakeCaseNamingConvention();
                })
                .AddLinq2Db();

            return builder;
        }
        
        /// <summary>
        /// Registers the Laraue.Apps.Identity client. With "MockExternalServices" set (local
        /// development) an in-process fake is used, so Identity doesn't have to run locally.
        /// </summary>
        public WebApplicationBuilder AddIdentityServices()
        {
            builder.Services
                .AddOptions<IdentityOptions>()
                .Bind(builder.Configuration.GetSection(nameof(IdentityOptions)));

            builder.Services
                .AddGrpcClient<UserIdentityService.UserIdentityServiceClient>((sp, o) =>
                {
                    o.Address = new Uri(sp.GetRequiredService<IOptions<IdentityOptions>>().Value.GrpcUrl);
                })
                .AddInterceptor(() => new ServiceIdInterceptor(ServiceId.LearnLanguage));

            if (builder.Configuration.GetValue<bool>("MockExternalServices"))
            {
                builder.Services
                    .AddSingleton<UserIdentityService.UserIdentityServiceClient, FakeUserIdentityServiceClient>();
            }

            return builder;
        }

        public WebApplicationBuilder AddApplicationServices()
        {
            builder.Services
                .AddSingleton<IDateTimeProvider, DateTimeProvider>()
                .AddSingleton<IRandomizer, Randomizer>()
                .AddMetrics()
                .AddSingleton<LearnLanguageMetrics>()
                .AddTelegramCore()
                .AddEfCoreUpdatesQueue<DatabaseContext>()
                .AddTelegramMiddleware<HandleExceptionsMiddleware>()
                .AddTelegramMiddleware<AutoCallbackResponseMiddleware>()
                .AddTelegramRequestLocalization<LocalizationProvider>()
                .Configure<TelegramRequestLocalizationOptions>(opt =>
                {
                    opt.AvailableLanguages = InterfaceLanguage.Available.Select(x => x.Code).ToArray();
                    opt.DefaultLanguage = InterfaceLanguage.Default.Code;
                })
                .AddTelegramAuthentication<Guid, TelegramUserQueryService, RequestContext>();

            builder.Services.UseUserRolesProvider<StaticUserRoleProvider>();

            builder.Services
                .AddScoped<IMenuService, MenuService>()

                .AddScoped<IWordsRepository, WordsRepository>()
                .AddScoped<IWordsWindowFactory, WordsWindowFactory>()

                .AddScoped<IStatsRepository, StatsRepository>()
                .AddScoped<IAdminRepository, AdminRepository>()

                .AddScoped<IUserSettingsService, UserSettingsService>()
                .AddScoped<IUserRepository, UserRepository>()

                .AddScoped<ISelectLanguageService, SelectLanguageService>()

                .AddScoped<IStatsService, StatsService>()

                .AddScoped<ILearnByCefrLevelService, LearnByCefrLevelService>()
                .AddScoped<ILearnByFirstLetterService, LearnByFirstLetterService>()
                .AddScoped<ILearnByTopicService, LearnByTopicService>()
                .AddScoped<ILearnByCefrLevelRepository, LearnByCefrLevelRepository>()
                .AddScoped<ILearnByTopicRepository, LearnByTopicRepository>()
                .AddScoped<ILearnByFirstLetterRepository, LearnByFirstLetterRepository>()

                .AddScoped<IQuizService, QuizService>()
                .AddScoped<QuizService.IRepository, QuizService.Repository>()
                .AddScoped<IQuestionsGenerator, QuestionsGenerator>();

            builder.Services.AddControllers();

            return builder;
        }

        private string? GetConnection(string connectionStringName)
        {
            return builder.Configuration.GetConnectionString(connectionStringName);
        }
    }
}