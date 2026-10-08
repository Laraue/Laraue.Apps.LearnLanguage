using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.LearnLanguage.AppServices.Identity;

/// <summary>
/// Settings for calling Laraue.Apps.Identity's internal gRPC API (global user identity).
/// </summary>
public class IdentityOptions
{
    /// <summary>
    /// Base address of Laraue.Apps.Identity.InternalApiHost's gRPC endpoint.
    /// </summary>
    [Required]
    [Url]
    public required string GrpcUrl { get; set; }

    /// <summary>
    /// How many users without a global id the backfill loads and processes per batch.
    /// </summary>
    [Range(1, 10_000)]
    public int BackfillBatchSize { get; set; } = 100;

    /// <summary>
    /// Pause between two Identity calls of the backfill, so Identity is not flooded.
    /// </summary>
    public TimeSpan BackfillThrottle { get; set; } = TimeSpan.FromMilliseconds(50);
}
