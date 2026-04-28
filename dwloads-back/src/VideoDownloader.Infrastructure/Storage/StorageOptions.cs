namespace VideoDownloader.Infrastructure.Storage;

public sealed class StorageOptions
{
    public string BasePath { get; init; } = Path.GetTempPath();
    public string BaseUrl { get; init; } = "http://localhost:5000";
    public TimeSpan FileRetention { get; init; } = TimeSpan.FromHours(1);
}
