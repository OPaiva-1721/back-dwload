using FluentAssertions;
using Hangfire;
using Hangfire.InMemory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using VideoDownloader.Application.Common.DTOs;
using VideoDownloader.Application.Common.Interfaces;
using VideoDownloader.Domain.Entities;
using VideoDownloader.Domain.Enums;
using VideoDownloader.Domain.Errors;
using VideoDownloader.Domain.Interfaces;
using VideoDownloader.Domain.ValueObjects;
using VideoDownloader.Infrastructure.Queue;
using VideoDownloader.Infrastructure.Storage;

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
            Options.Create(new StorageOptions()),
            NullLogger<DownloadJobProcessor>.Instance);
    }

    [Fact]
    public async Task ProcessAsync_WithNonExistingJob_ReturnsEarlyWithoutCallingDownloadService()
    {
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((DownloadJob?)null);

        await _processor.ProcessAsync(Guid.NewGuid(), CancellationToken.None);

        await _downloadService.DidNotReceive()
            .DownloadAsync(Arg.Any<Guid>(), Arg.Any<VideoUrl>(), Arg.Any<DownloadFormat>(), Arg.Any<string>(),
                Arg.Any<IProgress<int>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_WhenDownloadFails_FailsJobAndNotifiesFailure()
    {
        var job = MakeJob();
        var error = new Error("YtDlp.DownloadFailed", "Connection reset");
        _repository.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);
        _downloadService.DownloadAsync(
                Arg.Any<Guid>(), Arg.Any<VideoUrl>(), Arg.Any<DownloadFormat>(), Arg.Any<string>(),
                Arg.Any<IProgress<int>>(), Arg.Any<CancellationToken>())
            .Returns(Result<string>.Failure(error));

        await _processor.ProcessAsync(job.Id, CancellationToken.None);

        job.Status.Should().Be(DownloadStatus.Failed);
        job.ErrorMessage.Should().Be(error.Message);

        // Once when processing starts, once for the failure
        await _repository.Received(2).UpdateAsync(job, Arg.Any<CancellationToken>());
        await _notifier.Received(1).NotifyFailedAsync(job.Id, error.Message, Arg.Any<CancellationToken>());
        await _storage.DidNotReceive().ImportAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
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
                    Arg.Any<Guid>(), Arg.Any<VideoUrl>(), Arg.Any<DownloadFormat>(), Arg.Any<string>(),
                    Arg.Any<IProgress<int>>(), Arg.Any<CancellationToken>())
                .Returns(Result<string>.Success(tempFile));
            _storage.ImportAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(storedPath);
            _storage.GetDownloadUrl(storedPath, Arg.Any<string>()).Returns(downloadUrl);

            await _processor.ProcessAsync(job.Id, CancellationToken.None);

            job.Status.Should().Be(DownloadStatus.Completed);
            job.OutputFilePath.Should().Be(storedPath);

            await _notifier.Received(1).NotifyCompletedAsync(job.Id, Arg.Is<DownloadCompletedPayload>(p => p.DownloadUrl == downloadUrl), Arg.Any<CancellationToken>());
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ProcessAsync_WhileDownloadReportsProgress_DoesNotPersistProgressToRepository()
    {
        var job = MakeJob();
        _repository.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);
        _downloadService.DownloadAsync(
                Arg.Any<Guid>(), Arg.Any<VideoUrl>(), Arg.Any<DownloadFormat>(), Arg.Any<string>(),
                Arg.Any<IProgress<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var progress = ci.Arg<IProgress<int>>();
                for (var pct = 0; pct <= 99; pct++) progress.Report(pct);
                return Result<string>.Failure(new Error("YtDlp.DownloadFailed", "boom"));
            });

        await _processor.ProcessAsync(job.Id, CancellationToken.None);

        // Regression: progress used to fire a DB write per yt-dlp line on a shared DbContext,
        // throwing "A second operation was started on this context". Only start + final state now.
        await _repository.Received(2).UpdateAsync(job, Arg.Any<CancellationToken>());
        await _notifier.Received().NotifyProgressAsync(job.Id, 99, Arg.Any<CancellationToken>());
    }

    private static DownloadJob MakeJob()
    {
        var url = VideoUrl.Create("https://youtube.com/watch?v=test").Value!;
        return DownloadJob.Create(url, DownloadFormat.Mp4, "720p");
    }
}
