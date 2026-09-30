namespace VideoDownloader.Api.Contracts;

// Validated in RequestDownloadHandler (Application layer), where format and URL are checked too
public sealed record DownloadRequest(string Url, string Format, string Quality, string? Title = null, string? ThumbnailUrl = null, string? Duration = null);
