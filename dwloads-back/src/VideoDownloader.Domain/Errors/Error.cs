namespace VideoDownloader.Domain.Errors;

public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);
}

public static class DomainErrors
{
    public static class VideoUrl
    {
        public static readonly Error Empty = new("VideoUrl.Empty", "Paste a link to get started.");
        public static readonly Error Invalid = new("VideoUrl.Invalid", "That doesn't look like a valid link.");
        public static readonly Error UnsupportedPlatform = new("VideoUrl.UnsupportedPlatform",
            "This site isn't supported yet. Try YouTube, TikTok, Instagram, X, Vimeo, SoundCloud, Twitch or Dailymotion.");
    }

    public static class DownloadJob
    {
        public static readonly Error NotFound = new("DownloadJob.NotFound", "Download job not found.");
        public static readonly Error NotCancellable = new("DownloadJob.NotCancellable", "This download has already finished.");
    }
}
