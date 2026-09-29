using FluentAssertions;
using VideoDownloader.Domain.Enums;
using VideoDownloader.Domain.Errors;
using VideoDownloader.Domain.ValueObjects;

namespace VideoDownloader.Domain.Tests;

public sealed class VideoUrlTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptyOrWhitespace_ReturnsEmptyError(string? raw)
    {
        var result = VideoUrl.Create(raw!);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DomainErrors.VideoUrl.Empty);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("just-plain-text")]
    public void Create_WithInvalidUrl_ReturnsInvalidError(string raw)
    {
        var result = VideoUrl.Create(raw);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DomainErrors.VideoUrl.Invalid);
    }

    [Theory]
    [InlineData("https://example.com/123456")]
    [InlineData("https://facebook.com/video/abc")]
    public void Create_WithUnsupportedPlatform_ReturnsUnsupportedPlatformError(string raw)
    {
        var result = VideoUrl.Create(raw);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DomainErrors.VideoUrl.UnsupportedPlatform);
    }

    [Theory]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ", Platform.YouTube)]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", Platform.YouTube)]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", Platform.YouTube)]
    [InlineData("https://www.instagram.com/reel/abc123/", Platform.Instagram)]
    [InlineData("https://www.tiktok.com/@user/video/123", Platform.TikTok)]
    [InlineData("https://twitter.com/user/status/123", Platform.Twitter)]
    [InlineData("https://x.com/user/status/123", Platform.Twitter)]
    public void Create_WithSupportedPlatformUrl_ReturnsSuccessWithCorrectPlatform(string raw, Platform expected)
    {
        var result = VideoUrl.Create(raw);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Value.Should().Be(raw);
        result.Value.Platform.Should().Be(expected);
    }
}
