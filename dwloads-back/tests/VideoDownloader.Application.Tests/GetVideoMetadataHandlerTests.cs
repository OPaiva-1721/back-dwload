using FluentAssertions;
using NSubstitute;
using VideoDownloader.Application.Common.DTOs;
using VideoDownloader.Application.Common.Interfaces;
using VideoDownloader.Application.Downloads.Queries.GetVideoMetadata;
using VideoDownloader.Domain.Errors;
using VideoDownloader.Domain.ValueObjects;

namespace VideoDownloader.Application.Tests;

public sealed class GetVideoMetadataHandlerTests
{
    private readonly IVideoMetadataService _metadataService = Substitute.For<IVideoMetadataService>();
    private readonly GetVideoMetadataHandler _handler;

    public GetVideoMetadataHandlerTests()
    {
        _handler = new GetVideoMetadataHandler(_metadataService);
    }

    [Fact]
    public async Task Handle_WithValidUrl_DelegatesToServiceAndReturnsResult()
    {
        const string url = "https://youtube.com/watch?v=abc";
        var expected = new VideoMetadataResponse("My Video", "http://thumb.jpg", TimeSpan.FromSeconds(120), []);
        _metadataService.GetMetadataAsync(Arg.Any<VideoUrl>(), Arg.Any<CancellationToken>())
            .Returns(Result<VideoMetadataResponse>.Success(expected));

        var result = await _handler.Handle(new GetVideoMetadataQuery(url), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("https://vimeo.com/123")]
    public async Task Handle_WithInvalidOrUnsupportedUrl_ReturnsFailureWithoutCallingService(string url)
    {
        var result = await _handler.Handle(new GetVideoMetadataQuery(url), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await _metadataService.DidNotReceive()
            .GetMetadataAsync(Arg.Any<VideoUrl>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenServiceFails_ReturnsFailure()
    {
        const string url = "https://youtube.com/watch?v=abc";
        var error = new Error("YtDlp.MetadataFailed", "Could not retrieve video metadata.");
        _metadataService.GetMetadataAsync(Arg.Any<VideoUrl>(), Arg.Any<CancellationToken>())
            .Returns(Result<VideoMetadataResponse>.Failure(error));

        var result = await _handler.Handle(new GetVideoMetadataQuery(url), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }
}
