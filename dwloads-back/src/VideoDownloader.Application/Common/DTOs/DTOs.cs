namespace VideoDownloader.Application.Common.DTOs;

public sealed record RequestDownloadResponse(Guid JobId, string Status);

public sealed record DownloadCompletedPayload(
    string DownloadUrl,
    string Title,
    string ThumbnailUrl,
    string Duration,
    string Size,
    string ExpiresAt,
    DateTimeOffset ExpiresAtUtc);

public sealed record VideoMetadataResponse(
    string Title,
    string ThumbnailUrl,
    TimeSpan Duration,
    IReadOnlyList<FormatInfo> AvailableFormats);

/// <param name="FileSizeBytes">Exact size, or yt-dlp's estimate when the exact one is unknown.</param>
/// <param name="HasVideo">False for audio-only streams (and storyboards are excluded entirely).</param>
/// <param name="AudioBitrateKbps">Source audio bitrate, so clients don't offer pointless upsampling.</param>
public sealed record FormatInfo(
    string Id,
    string Extension,
    string Quality,
    long? FileSizeBytes,
    int? Height,
    bool HasVideo = false,
    bool HasAudio = false,
    double? AudioBitrateKbps = null);

public sealed record DownloadJobStatusResponse(
    Guid JobId,
    string Status,
    int ProgressPercent,
    string? DownloadUrl,
    string? ErrorMessage,
    string Title = "",
    string ThumbnailUrl = "",
    string Duration = "",
    long FileSizeBytes = 0,
    DateTimeOffset? CompletedAt = null);

public enum DownloadStep { Downloading, Converting }

/// <summary>Overall job progress: which phase yt-dlp is in and how far along (0-99).</summary>
public sealed record DownloadProgress(DownloadStep Step, int Percent);
