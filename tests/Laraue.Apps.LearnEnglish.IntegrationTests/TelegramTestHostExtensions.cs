using Laraue.Apps.LearnLanguage.DataAccess;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.LearnEnglish.IntegrationTests;

public static class TelegramTestHostExtensions
{
    extension(AppTelegramTestHost telegramTestHost)
    {
        public T GetFromDb<T>(Func<DatabaseContext, T> getData) where T : class
        {
            using var scope = telegramTestHost.CreateScope();
            
            var dbContext = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            
            return getData(dbContext);
        }
        
        public void InsertIntoDb<T>(T entity) where T : class
        {
            using var scope = telegramTestHost.CreateScope();
            
            var dbContext = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            
            dbContext.Add(entity);
            
            dbContext.SaveChanges();
        }

        public void InsertRangeIntoDb<T>(IEnumerable<T> entities) where T : class
        {
            using var scope = telegramTestHost.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

            dbContext.AddRange(entities);

            dbContext.SaveChanges();
        }
    }
}