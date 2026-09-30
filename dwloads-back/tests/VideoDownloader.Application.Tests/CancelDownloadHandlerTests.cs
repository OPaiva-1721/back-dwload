using FluentAssertions;
using NSubstitute;
using VideoDownloader.Application.Common.Interfaces;
using VideoDownloader.Application.Downloads.Commands.CancelDownload;
using VideoDownloader.Domain.Entities;
using VideoDownloader.Domain.Enums;
using VideoDownloader.Domain.Errors;
using VideoDownloader.Domain.Interfaces;
using VideoDownloader.Domain.ValueObjects;

namespace VideoDownloader.Application.Tests;

public sealed class CancelDownloadHandlerTests
{
    private readonly IDownloadJobRepository _repository = Substitute.For<IDownloadJobRepository>();
    private readonly IDownloadCanceller _canceller = Substitute.For<IDownloadCanceller>();
    private readonly IProgressNotifier _notifier = Substitute.For<IProgressNotifier>();
    private readonly CancelDownloadHandler _handler;

    public CancelDownloadHandlerTests() =>
        _handler = new CancelDownloadHandler(_repository, _canceller, _notifier);

    private DownloadJob Given(Action<DownloadJob>? setup = null)
    {
        var job = DownloadJob.Create(VideoUrl.Create("https://youtube.com/watch?v=abc").Value!, DownloadFormat.Mp4, "720p");
        setup?.Invoke(job);
        _repository.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);
        return job;
    }

    [Fact]
    public async Task Handle_UnknownJob_ReturnsNotFound()
    {
        var result = await _handler.Handle(new CancelDownloadCommand(Guid.NewGuid()), CancellationToken.None);

        result.Error.Should().Be(DomainErrors.DownloadJob.NotFound);
    }

    [Fact]
    public async Task Handle_FinishedJob_ReturnsNotCancellable()
    {
        var job = Given(j => j.Complete("/f.mp4"));

        var result = await _handler.Handle(new CancelDownloadCommand(job.Id), CancellationToken.None);

        result.Error.Should().Be(DomainErrors.DownloadJob.NotCancellable);
        _canceller.DidNotReceiveWithAnyArgs().TryCancel(default);
    }

    [Fact]
    public async Task Handle_RunningJob_SignalsProcessorAndLeavesStateToIt()
    {
        var job = Given(j => j.UpdateProgress(40));
        _canceller.TryCancel(job.Id).Returns(true);

        var result = await _handler.Handle(new CancelDownloadCommand(job.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        job.Status.Should().Be(DownloadStatus.Processing, "the processor records the cancellation itself");
        await _repository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task Handle_QueuedJob_MarksCancelledAndNotifies()
    {
        var job = Given();
        _canceller.TryCancel(job.Id).Returns(false);

        var result = await _handler.Handle(new CancelDownloadCommand(job.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        job.Status.Should().Be(DownloadStatus.Cancelled);
        await _repository.Received(1).UpdateAsync(job, Arg.Any<CancellationToken>());
        await _notifier.Received(1).NotifyCancelledAsync(job.Id, Arg.Any<CancellationToken>());
    }
}
