namespace VideoDownloader.Infrastructure.Storage;

public sealed class StorageOptions
{
    public string BasePath { get; init; } = Path.GetTempPath();
    public string BaseUrl { get; init; } = "http://localhost:5000";
    public TimeSpan FileRetention { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Scratch directory for in-progress downloads. Lives inside <see cref="BasePath"/> so it is on
    /// the same filesystem (same Docker volume), making the final move a rename instead of a copy.
    /// </summary>
    public string WorkPath => Path.Combine(BasePath, ".work");
}
