using VideoDownloader.Domain.Enums;
using VideoDownloader.Domain.ValueObjects;

namespace VideoDownloader.Domain.Entities;

public sealed class DownloadJob
{
    public Guid Id { get; private set; }
    public VideoUrl Url { get; private set; } = null!;
    public DownloadFormat Format { get; private set; }
    public string Quality { get; private set; } = string.Empty;
    public DownloadStatus Status { get; private set; }
    public int ProgressPercent { get; private set; }
    public string? OutputFilePath { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string ThumbnailUrl { get; private set; } = string.Empty;
    public string Duration { get; private set; } = string.Empty;
    public long FileSizeBytes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private DownloadJob() { }

    public static DownloadJob Create(VideoUrl url, DownloadFormat format, string quality, string? title = null, string? thumbnailUrl = null, string? duration = null) => new()
    {
        Id = Guid.NewGuid(),
        Url = url,
        Format = format,
        Quality = quality,
        Status = DownloadStatus.Queued,
        CreatedAt = DateTimeOffset.UtcNow,
        Title = title ?? string.Empty,
        ThumbnailUrl = thumbnailUrl ?? string.Empty,
        Duration = duration ?? string.Empty,
    };

    public void UpdateProgress(int percent)
    {
        if (percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        ProgressPercent = percent;
        Status = DownloadStatus.Processing;
    }

    public void Complete(string filePath, string title = "", string thumbnailUrl = "", string duration = "", long fileSizeBytes = 0)
    {
        OutputFilePath = filePath;
        Status = DownloadStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
        ProgressPercent = 100;
        Title = title;
        ThumbnailUrl = thumbnailUrl;
        Duration = duration;
        FileSizeBytes = fileSizeBytes;
    }

    public void Fail(string reason)
    {
        ErrorMessage = reason;
        Status = DownloadStatus.Failed;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
