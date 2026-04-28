using FluentAssertions;
using Hangfire;
using Hangfire.InMemory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VideoDownloader.Application.Common.Interfaces;
using VideoDownloader.Domain.Entities;
using VideoDownloader.Domain.Enums;
using VideoDownloader.Domain.Errors;
using VideoDownloader.Domain.Interfaces;
using VideoDownloader.Domain.ValueObjects;
using VideoDownloader.Infrastructure.Queue;

namespace VideoDownloader.Infrastructure.Tests;

public sealed class DownloadJobProcessorTests
{
    private readonly IDownloadJobRepository _repository = Substitute.For<IDownloadJobRepository>();
    private readonly IVideoDownloadService _downloadService = Substitute.For<IVideoDownloadService>();
    private readonly IStorageService _storage = Substitute.For<IStorageService>();
    private readonly IProgressNotifier _notifier = Substitute.For<IProgressNotifier>();
    private readonly DownloadJobProcessor _processor;

    public DownloadJobProcessorTests()
    {
        JobStorage.Current = new InMemoryStorage();
        _processor = new DownloadJobProcessor(
            _repository,
            _downloadService,
            _storage,
            _notifier,
            NullLogger<DownloadJobProcessor>.Instance);
    }

    [Fact]
    public async Task ProcessAsync_WithNonExistingJob_ReturnsEarlyWithoutCallingDownloadService()
    {
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((DownloadJob?)null);

        await _processor.ProcessAsync(Guid.NewGuid(), CancellationToken.None);

        await _downloadService.DidNotReceive()
            .DownloadAsync(Arg.Any<Guid>(), Arg.Any<VideoUrl>(), Arg.Any<DownloadFormat>(),
                Arg.Any<IProgress<int>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_WhenDownloadFails_FailsJobAndNotifiesFailure()
    {
        var job = MakeJob();
        var error = new Error("YtDlp.DownloadFailed", "Connection reset");
        _repository.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);
        _downloadService.DownloadAsync(
                Arg.Any<Guid>(), Arg.Any<VideoUrl>(), Arg.Any<DownloadFormat>(),
                Arg.Any<IProgress<int>>(), Arg.Any<CancellationToken>())
            .Returns(Result<string>.Failure(error));

        await _processor.ProcessAsync(job.Id, CancellationToken.None);

        job.Status.Should().Be(DownloadStatus.Failed);
        job.ErrorMessage.Should().Be(error.Message);

        await _repository.Received(1).UpdateAsync(
            Arg.Is<DownloadJob>(j => j.Status == DownloadStatus.Failed),
            Arg.Any<CancellationToken>());
        await _notifier.Received(1).NotifyFailedAsync(job.Id, error.Message, Arg.Any<CancellationToken>());
        await _storage.DidNotReceive().SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_WhenDownloadSucceeds_CompletesJobAndNotifiesCompletion()
    {
        var job = MakeJob();
        var tempFile = Path.GetTempFileName();
        const string storedPath = "/storage/output.mp4";
        const string downloadUrl = "http://localhost/files/output.mp4";

        try
        {
            await File.WriteAllBytesAsync(tempFile, [1, 2, 3]);

            _repository.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);
            _downloadService.DownloadAsync(
                    Arg.Any<Guid>(), Arg.Any<VideoUrl>(), Arg.Any<DownloadFormat>(),
                    Arg.Any<IProgress<int>>(), Arg.Any<CancellationToken>())
                .Returns(Result<string>.Success(tempFile));
            _storage.SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(storedPath);
            _storage.GetDownloadUrl(storedPath).Returns(downloadUrl);

            await _processor.ProcessAsync(job.Id, CancellationToken.None);

            job.Status.Should().Be(DownloadStatus.Completed);
            job.OutputFilePath.Should().Be(storedPath);

            await _notifier.Received(1).NotifyCompletedAsync(job.Id, downloadUrl, Arg.Any<CancellationToken>());
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private static DownloadJob MakeJob()
    {
        var url = VideoUrl.Create("https://youtube.com/watch?v=test").Value!;
        return DownloadJob.Create(url, DownloadFormat.Mp4);
    }
}
