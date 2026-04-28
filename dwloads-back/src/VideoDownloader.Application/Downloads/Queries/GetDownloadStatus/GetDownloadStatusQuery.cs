using MediatR;
using VideoDownloader.Application.Common.DTOs;
using VideoDownloader.Domain.Errors;

namespace VideoDownloader.Application.Downloads.Queries.GetDownloadStatus;

public sealed record GetDownloadStatusQuery(Guid JobId)
    : IRequest<Result<DownloadJobStatusResponse>>;
