// See https://aka.ms/new-console-template for more information

using Laraue.Apps.LearnLanguage.AutoTranslator;
using Laraue.Apps.LearnLanguage.AutoTranslator.Services;
using Laraue.Ollama.NET;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddJsonFile("appsettings.Development.json", optional: true)
    .Build();

Console.OutputEncoding = System.Text.Encoding.UTF8;

var serviceCollection = new ServiceCollection()
    .AddLogging(x => x.AddJsonConsole())
    .AddSingleton<IWordsService, WordsService>()
    .AddSingleton<IWordsAutoTranslator, WordsAutoTranslator>()
    .AddScoped<IResxFilesTranslator, ResxFilesTranslator>()
    .AddSingleton<IConfiguration>(configuration);

serviceCollection.AddOptions<ServiceOptions>();
serviceCollection.Configure<ServiceOptions>(configuration.GetRequiredSection("ServiceOptions"));

serviceCollection.AddHttpClient<IOllamaPredictor, OllamaPredictor>(x =>
{
    x.BaseAddress = new Uri("http://localhost:11434/");
    x.Timeout = TimeSpan.FromSeconds(300);
});

serviceCollection.AddHttpClient<IAutoTranslator, OllamaAutoTranslator>(x =>
{
    x.BaseAddress = new Uri("http://localhost:11434/");
    x.Timeout = TimeSpan.FromSeconds(300);
});

var services = serviceCollection.BuildServiceProvider();
var options = services.GetRequiredService<IOptions<ServiceOptions>>();

Console.WriteLine("1 - Translate words");
Console.WriteLine("2 - Translate .resx files");

var result = Console.ReadLine();
switch (result)
{
    case "1":
        var autoTranslator = services.GetRequiredService<IWordsAutoTranslator>();
        await autoTranslator.RunAsync();
        break;
    case "2":
        var resxFilesTranslator = services.GetRequiredService<IResxFilesTranslator>();
        await resxFilesTranslator.TranslateFileAsync(options.Value.ResourcesPath, "QuizMode");
        break;
    default:
        throw new InvalidOperationException();
}