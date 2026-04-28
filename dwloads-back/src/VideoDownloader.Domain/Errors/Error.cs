namespace VideoDownloader.Domain.Errors;

public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);
}

public static class DomainErrors
{
    public static class VideoUrl
    {
        public static readonly Error Empty = new("VideoUrl.Empty", "URL cannot be empty.");
        public static readonly Error Invalid = new("VideoUrl.Invalid", "URL format is invalid.");
        public static readonly Error UnsupportedPlatform = new("VideoUrl.UnsupportedPlatform", "Platform not supported.");
    }

    public static class DownloadJob
    {
        public static readonly Error NotFound = new("DownloadJob.NotFound", "Download job not found.");
    }
}
