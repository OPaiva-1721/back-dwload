namespace VideoDownloader.Application.Common.DTOs;

public sealed record RequestDownloadResponse(Guid JobId, string Status);

public sealed record DownloadCompletedPayload(
    string DownloadUrl,
    string Title,
    string ThumbnailUrl,
    string Duration,
    string Size,
    string ExpiresAt);

public sealed record VideoMetadataResponse(
    string Title,
    string ThumbnailUrl,
    TimeSpan Duration,
    IReadOnlyList<FormatInfo> AvailableFormats);

public sealed record FormatInfo(string Id, string Extension, string Quality, long? FileSizeBytes, int? Height);

public sealed record DownloadJobStatusResponse(
    Guid JobId,
    string Status,
    int ProgressPercent,
    string? DownloadUrl,
    string? ErrorMessage,
    string Title = "",
    string ThumbnailUrl = "",
    string Duration = "",
    long FileSizeBytes = 0);
