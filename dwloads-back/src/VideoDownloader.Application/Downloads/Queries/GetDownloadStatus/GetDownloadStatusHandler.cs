using MediatR;
using VideoDownloader.Application.Common.DTOs;
using VideoDownloader.Application.Common.Interfaces;
using VideoDownloader.Domain.Enums;
using VideoDownloader.Domain.Errors;
using VideoDownloader.Domain.Interfaces;

namespace VideoDownloader.Application.Downloads.Queries.GetDownloadStatus;

public sealed class GetDownloadStatusHandler(
    IDownloadJobRepository repository,
    IStorageService storage)
    : IRequestHandler<GetDownloadStatusQuery, Result<DownloadJobStatusResponse>>
{
    public async Task<Result<DownloadJobStatusResponse>> Handle(
        GetDownloadStatusQuery request, CancellationToken ct)
    {
        var job = await repository.GetByIdAsync(request.JobId, ct);
        if (job is null)
            return Result<DownloadJobStatusResponse>.Failure(DomainErrors.DownloadJob.NotFound);

        var downloadUrl = job.Status == DownloadStatus.Completed && job.OutputFilePath is not null
            ? storage.GetDownloadUrl(job.OutputFilePath, job.Title)
            : null;

        return Result<DownloadJobStatusResponse>.Success(new DownloadJobStatusResponse(
            job.Id,
            job.Status.ToString(),
            job.ProgressPercent,
            downloadUrl,
            job.ErrorMessage,
            job.Title,
            job.ThumbnailUrl,
            job.Duration,
            job.FileSizeBytes));
    }
}
