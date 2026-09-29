using FluentAssertions;
using NSubstitute;
using VideoDownloader.Application.Common.Interfaces;
using VideoDownloader.Application.Downloads.Queries.GetDownloadStatus;
using VideoDownloader.Domain.Entities;
using VideoDownloader.Domain.Enums;
using VideoDownloader.Domain.Interfaces;
using VideoDownloader.Domain.ValueObjects;

namespace VideoDownloader.Application.Tests;

public sealed class GetDownloadStatusHandlerTests
{
    private readonly IDownloadJobRepository _repository = Substitute.For<IDownloadJobRepository>();
    private readonly IStorageService _storage = Substitute.For<IStorageService>();
    private readonly GetDownloadStatusHandler _handler;

    public GetDownloadStatusHandlerTests()
    {
        _handler = new GetDownloadStatusHandler(_repository, _storage);
    }

    [Fact]
    public async Task Handle_WithNonExistingJob_ReturnsNotFoundError()
    {
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((DownloadJob?)null);

        var result = await _handler.Handle(new GetDownloadStatusQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DownloadJob.NotFound");
    }

    [Fact]
    public async Task Handle_WithQueuedJob_ReturnsStatusWithoutDownloadUrl()
    {
        var job = MakeJob();
        _repository.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);

        var result = await _handler.Handle(new GetDownloadStatusQuery(job.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(DownloadStatus.Queued.ToString());
        result.Value.DownloadUrl.Should().BeNull();
        result.Value.ProgressPercent.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithProcessingJob_ReturnsProgress()
    {
        var job = MakeJob();
        job.UpdateProgress(55);
        _repository.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);

        var result = await _handler.Handle(new GetDownloadStatusQuery(job.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(DownloadStatus.Processing.ToString());
        result.Value.ProgressPercent.Should().Be(55);
        result.Value.DownloadUrl.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithCompletedJob_ReturnsDownloadUrl()
    {
        const string filePath = "/storage/abc.mp4";
        const string downloadUrl = "http://localhost/files/abc.mp4";
        var job = MakeJob();
        job.Complete(filePath);
        _repository.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);
        _storage.GetDownloadUrl(filePath).Returns(downloadUrl);

        var result = await _handler.Handle(new GetDownloadStatusQuery(job.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(DownloadStatus.Completed.ToString());
        result.Value.DownloadUrl.Should().Be(downloadUrl);
        result.Value.ProgressPercent.Should().Be(100);
    }

    [Fact]
    public async Task Handle_WithFailedJob_ReturnsErrorMessage()
    {
        var job = MakeJob();
        job.Fail("yt-dlp crashed");
        _repository.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);

        var result = await _handler.Handle(new GetDownloadStatusQuery(job.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(DownloadStatus.Failed.ToString());
        result.Value.ErrorMessage.Should().Be("yt-dlp crashed");
        result.Value.DownloadUrl.Should().BeNull();
    }

    private static DownloadJob MakeJob()
    {
        var url = VideoUrl.Create("https://youtube.com/watch?v=abc").Value!;
        return DownloadJob.Create(url, DownloadFormat.Mp4, "720p");
    }
}
