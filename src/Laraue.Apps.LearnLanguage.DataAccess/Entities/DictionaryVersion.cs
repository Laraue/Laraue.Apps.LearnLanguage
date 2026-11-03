using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.LearnLanguage.DataAccess.Entities;

/// <summary>
/// Which version of the dictionary files <see cref="DictionarySync"/> last applied. A single row.
/// </summary>
public sealed class DictionaryVersion
{
    public const int SingleRowId = 1;

    public int Id { get; set; }

    /// <summary>
    /// SHA-256 of the dictionary files, hex encoded.
    /// </summary>
    [MaxLength(64)]
    public required string Hash { get; set; }

    /// <summary>
    /// When the files were applied (UTC).
    /// </summary>
    public DateTime SyncedAt { get; set; }
}
