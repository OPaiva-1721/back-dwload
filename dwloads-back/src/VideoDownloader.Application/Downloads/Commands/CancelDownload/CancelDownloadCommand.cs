using MediatR;
using VideoDownloader.Domain.Errors;

namespace VideoDownloader.Application.Downloads.Commands.CancelDownload;

public sealed record CancelDownloadCommand(Guid JobId) : IRequest<Result<bool>>;
