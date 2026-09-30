using System.Text.Json;
using FluentAssertions;
using VideoDownloader.Infrastructure.YtDlp;

namespace VideoDownloader.Infrastructure.Tests;

public sealed class YtDlpVideoServiceTests
{
    [Theory]
    [InlineData("ERROR: [youtube] abc: Sign in to confirm your age. This video may be inappropriate", "YtDlp.AgeRestricted")]
    [InlineData("ERROR: [youtube] abc: Sign in to confirm you're not a bot", "YtDlp.Blocked")]
    [InlineData("ERROR: [youtube] abc: Private video. Sign in if you've been granted access", "YtDlp.Private")]
    [InlineData("ERROR: [youtube] xxxxxxxxxxx: Video unavailable", "YtDlp.Unavailable")]
    [InlineData("ERROR: [youtube] abc: This live event will begin in 3 hours.", "YtDlp.Live")]
    [InlineData("ERROR: unable to download video data: HTTP Error 403: Forbidden", "YtDlp.Blocked")]
    [InlineData("ERROR: [generic] Unsupported URL: https://example.com", "YtDlp.NoMedia")]
    [InlineData("ERROR: Requested format is not available", "YtDlp.FormatUnavailable")]
    public void ClassifyFailure_MapsKnownYtDlpErrorsToUserFacingCodes(string stderr, string expectedCode)
    {
        var error = YtDlpVideoService.ClassifyFailure(stderr);

        error.Should().NotBeNull();
        error!.Code.Should().Be(expectedCode);
        error.Message.Should().NotContain("yt-dlp").And.NotContain("stderr");
    }

    [Fact]
    public void ClassifyFailure_UnknownError_ReturnsNull()
    {
        YtDlpVideoService.ClassifyFailure("ERROR: something nobody has seen before").Should().BeNull();
    }

    [Theory]
    [InlineData("360p", "height<=360")]
    [InlineData("1440p", "height<=1440")]
    public void VideoFormatSelector_UsesRequestedHeight(string quality, string expected)
    {
        YtDlpVideoService.VideoFormatSelector(quality).Should().Contain(expected);
    }

    [Fact]
    public void VideoFormatSelector_UnparseableQuality_FallsBackToBest()
    {
        YtDlpVideoService.VideoFormatSelector("best").Should().NotContain("height");
    }

    [Fact]
    public void ToFormatInfo_ReadsStreamKindsBitrateAndApproximateSize()
    {
        using var doc = JsonDocument.Parse("""
            [
              { "format_id": "140", "ext": "m4a", "vcodec": "none", "acodec": "mp4a.40.2", "abr": 129.5, "filesize": 3500000 },
              { "format_id": "137", "ext": "mp4", "vcodec": "avc1", "acodec": "none", "height": 1080, "filesize_approx": 18000000 }
            ]
            """);
        var audio = YtDlpVideoService.ToFormatInfo(doc.RootElement[0]);
        var video = YtDlpVideoService.ToFormatInfo(doc.RootElement[1]);

        audio.HasAudio.Should().BeTrue();
        audio.HasVideo.Should().BeFalse();
        audio.AudioBitrateKbps.Should().Be(129.5);
        audio.FileSizeBytes.Should().Be(3_500_000);

        video.HasVideo.Should().BeTrue();
        video.HasAudio.Should().BeFalse();
        video.Height.Should().Be(1080);
        video.FileSizeBytes.Should().Be(18_000_000, "filesize_approx is used when the exact size is unknown");
        video.AudioBitrateKbps.Should().BeNull();
    }
}
