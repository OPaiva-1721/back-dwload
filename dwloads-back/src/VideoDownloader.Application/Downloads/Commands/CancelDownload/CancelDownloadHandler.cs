using MediatR;
using VideoDownloader.Application.Common.Interfaces;
using VideoDownloader.Domain.Errors;
using VideoDownloader.Domain.Interfaces;

namespace VideoDownloader.Application.Downloads.Commands.CancelDownload;

public sealed class CancelDownloadHandler(
    IDownloadJobRepository repository,
    IDownloadCanceller canceller,
    IProgressNotifier notifier)
    : IRequestHandler<CancelDownloadCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(CancelDownloadCommand request, CancellationToken ct)
    {
        var job = await repository.GetByIdAsync(request.JobId, ct);
        if (job is null)
            return Result<bool>.Failure(DomainErrors.DownloadJob.NotFound);

        if (job.IsFinished)
            return Result<bool>.Failure(DomainErrors.DownloadJob.NotCancellable);

        // A job its processor has picked up is stopped by that processor, which then records the
        // cancellation itself — writing it here too would race the processor's own save.
        if (canceller.TryCancel(job.Id))
            return Result<bool>.Success(true);

        // Still queued (or its worker is gone): mark it so the processor skips it when picked up
        job.Cancel();
        await repository.UpdateAsync(job, ct);
        await notifier.NotifyCancelledAsync(job.Id, ct);
        return Result<bool>.Success(true);
    }
}
