using Laraue.Apps.Identity.Internal.Contracts;
using Laraue.Apps.LearnLanguage.AppServices.Identity;
using Laraue.Apps.LearnLanguage.AppServices.Services;
using Laraue.Apps.LearnLanguage.Host;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Telegram.Bot.Types;
using Xunit;

namespace Laraue.Apps.LearnEnglish.IntegrationTests;

[Collection("IntegrationTest")]
public class IntegrationTest
{
    protected static AppTelegramTestHost GetTelegramTestHost()
    {
        var builder = WebApplication.CreateBuilder();

        builder.Configuration.AddJsonFile("appsettings.json");
            
        builder
            .AddTelegramOptions("Telegram")
            .AddIdentityServices()
            .AddApplicationServices()
            .AddDatabaseServices("Postgre");
        
        var appServices = builder
            .Services
            .Replace(
                ServiceDescriptor.Singleton<UserIdentityService.UserIdentityServiceClient, FakeUserIdentityServiceClient>())
            .Replace(
                new ServiceDescriptor(
                    typeof(IRandomizer), 
                    null,
                    typeof(DeterminedRandomizer),
                    ServiceLifetime.Singleton));
        
        return new AppTelegramTestHost(appServices);
    }

    protected static User DefaultUser => new()
    {
        Id = 1,
        Username = "user1",
    };

    private class DeterminedRandomizer : IRandomizer
    {
        private int _nextRandomValue;
        
        public IQueryable<NewQuestionDto> InRandomOrder(IQueryable<NewQuestionDto> queryable)
        {
            return queryable
                .OrderBy(x => x.WordId)
                .ThenBy(x => x.PartOfSpeechId);
        }

        public IEnumerable<NewQuestionDto> InRandomOrder(IEnumerable<NewQuestionDto> enumerable)
        {
            return InRandomOrder(enumerable.AsQueryable());
        }

        public int NextRandomValue(int minValue, int maxValue)
        {
            if (_nextRandomValue < minValue)
            {
                _nextRandomValue = minValue;
            }

            if (_nextRandomValue > maxValue)
            {
                _nextRandomValue = minValue;
            }
            
            return _nextRandomValue++;
        }
    }
}

