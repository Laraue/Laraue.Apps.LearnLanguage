using Xunit;

namespace Laraue.Apps.LearnEnglish.IntegrationTests;

/// <summary>
/// The host applies migrations at startup, and EF builds a migration's target model in memory to do it. When the
/// whole dictionary was seeded with HasData that model was ~16 MB of code and didn't fit the production
/// container (MIN-18, MIN-25). This catches a migration or a snapshot that grows like that again before it ships.
/// </summary>
public class MigrationSizeTests
{
    private const long MaxFileSize = 1024 * 1024;

    private const string SnapshotFileName = "DatabaseContextModelSnapshot.cs";

    /// <summary>
    /// Id of the last migration from the HasData days (AddTelegramFirstLastName). It and the ones before it
    /// still carry the full model.
    /// </summary>
    private const string LastOversizedMigrationId = "20260826081046";

    [Fact]
    public void NewMigrationsAndSnapshot_AreSmall()
    {
        var migrationsDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Laraue.Apps.LearnLanguage.DataAccess",
            "Migrations");

        var checkedFiles = Directory
            .GetFiles(migrationsDirectory, "*.cs")
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return name == SnapshotFileName
                    || string.CompareOrdinal(name[..LastOversizedMigrationId.Length], LastOversizedMigrationId) > 0;
            })
            .ToArray();

        Assert.Contains(checkedFiles, path => Path.GetFileName(path) == SnapshotFileName);

        var oversized = checkedFiles
            .Select(path => new FileInfo(path))
            .Where(file => file.Length > MaxFileSize)
            .Select(file => $"{file.Name} ({file.Length / 1024} KB)")
            .ToArray();

        Assert.True(
            oversized.Length == 0,
            $"Migration files over {MaxFileSize / 1024} KB: {string.Join(", ", oversized)}. " +
            "Is something large seeded with HasData again?");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Laraue.Apps.LearnLanguage.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Laraue.Apps.LearnLanguage.sln not found above " + AppContext.BaseDirectory);
    }
}
