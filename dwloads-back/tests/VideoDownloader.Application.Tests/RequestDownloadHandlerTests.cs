using FluentAssertions;
using NSubstitute;
using VideoDownloader.Application.Common.Interfaces;
using VideoDownloader.Application.Downloads.Commands.RequestDownload;
using VideoDownloader.Domain.Enums;
using VideoDownloader.Domain.Interfaces;

namespace VideoDownloader.Application.Tests;

public sealed class RequestDownloadHandlerTests
{
    private readonly IDownloadJobRepository _repository = Substitute.For<IDownloadJobRepository>();
    private readonly IDownloadQueue _queue = Substitute.For<IDownloadQueue>();
    private readonly RequestDownloadHandler _handler;

    public RequestDownloadHandlerTests()
    {
        _handler = new RequestDownloadHandler(_repository, _queue);
    }

    [Theory]
    [InlineData("https://youtube.com/watch?v=abc", "mp4")]
    [InlineData("https://youtu.be/abc", "mp3")]
    [InlineData("https://instagram.com/reel/abc/", "mp4")]
    [InlineData("https://tiktok.com/@user/video/123", "mp4")]
    [InlineData("https://twitter.com/user/status/123", "mp3")]
    public async Task Handle_WithValidRequest_EnqueuesJobAndReturnsSuccess(string url, string format)
    {
        var command = new RequestDownloadCommand(url, format, format == "mp3" ? "192kbps" : "720p");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.JobId.Should().NotBeEmpty();
        result.Value.Status.Should().Be(DownloadStatus.Queued.ToString());

        await _repository.Received(1).AddAsync(Arg.Any<Domain.Entities.DownloadJob>(), Arg.Any<CancellationToken>());
        await _queue.Received(1).EnqueueAsync(result.Value.JobId, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_WithEmptyUrl_ReturnsFailure(string url)
    {
        var command = new RequestDownloadCommand(url, "mp4", "720p");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VideoUrl.Empty");
    }

    [Fact]
    public async Task Handle_WithInvalidUrl_ReturnsFailure()
    {
        var command = new RequestDownloadCommand("not-a-url", "mp4", "720p");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VideoUrl.Invalid");
    }

    [Fact]
    public async Task Handle_WithUnsupportedPlatform_ReturnsFailure()
    {
        var command = new RequestDownloadCommand("https://example.com/123", "mp4", "720p");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VideoUrl.UnsupportedPlatform");
    }

    [Theory]
    [InlineData("avi")]
    [InlineData("wav")]
    [InlineData("")]
    [InlineData("invalid")]
    public async Task Handle_WithInvalidFormat_ReturnsFailure(string format)
    {
        var command = new RequestDownloadCommand("https://youtube.com/watch?v=abc", format, "720p");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Format.Invalid");
    }

    [Fact]
    public async Task Handle_WithValidRequest_DoesNotEnqueueWhenRepositoryFails()
    {
        _repository.AddAsync(Arg.Any<Domain.Entities.DownloadJob>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new Exception("DB error")));

        var command = new RequestDownloadCommand("https://youtube.com/watch?v=abc", "mp4", "720p");

        var act = async () => await _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<Exception>().WithMessage("DB error");
        await _queue.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("mp4", "360p")]
    [InlineData("mp4", "1440p")]
    [InlineData("mp4", "144p")]
    [InlineData("mp3", "192kbps")]
    public async Task Handle_WithRealStreamQuality_Succeeds(string format, string quality)
    {
        var result = await _handler.Handle(
            new RequestDownloadCommand("https://youtube.com/watch?v=abc", format, quality), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("mp4", "1080")]
    [InlineData("mp4", "99p")]
    [InlineData("mp4", "9000p")]
    [InlineData("mp4", "192kbps")]
    [InlineData("mp3", "1080p")]
    [InlineData("mp3", "64kbps")]
    public async Task Handle_WithInvalidQuality_ReturnsFailureWithoutEnqueueing(string format, string quality)
    {
        var result = await _handler.Handle(
            new RequestDownloadCommand("https://youtube.com/watch?v=abc", format, quality), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Quality.Invalid");
        await _queue.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default);
    }
}
