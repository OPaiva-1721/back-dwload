namespace VideoDownloader.Infrastructure.YtDlp;

public sealed class YtDlpOptions
{
    public string ExecutablePath { get; init; } = "yt-dlp";
    public string? CookiesFile { get; init; }
}
