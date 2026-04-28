using VideoDownloader.Domain.Errors;
using VideoDownloader.Domain.Enums;

namespace VideoDownloader.Domain.ValueObjects;

public sealed record VideoUrl
{
    public string Value { get; }
    public Platform Platform { get; }

    private static readonly IReadOnlyDictionary<string, Platform> PlatformMap = new Dictionary<string, Platform>
    {
        ["youtube.com"]    = Platform.YouTube,
        ["youtu.be"]       = Platform.YouTube,
        ["instagram.com"]  = Platform.Instagram,
        ["tiktok.com"]     = Platform.TikTok,
        ["twitter.com"]    = Platform.Twitter,
        ["x.com"]          = Platform.Twitter,
        ["vimeo.com"]      = Platform.Vimeo,
        ["twitch.tv"]      = Platform.Twitch,
        ["soundcloud.com"] = Platform.SoundCloud,
        ["spotify.com"]    = Platform.Spotify,
        ["dailymotion.com"]= Platform.Dailymotion,
    };

    private VideoUrl(string value, Platform platform)
    {
        Value = value;
        Platform = platform;
    }

    public static Result<VideoUrl> Create(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Result<VideoUrl>.Failure(DomainErrors.VideoUrl.Empty);

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            return Result<VideoUrl>.Failure(DomainErrors.VideoUrl.Invalid);

        var host = uri.Host.Replace("www.", "");
        if (!PlatformMap.TryGetValue(host, out var platform))
            return Result<VideoUrl>.Failure(DomainErrors.VideoUrl.UnsupportedPlatform);

        return Result<VideoUrl>.Success(new VideoUrl(raw, platform));
    }
}
