using MediatR;
using VideoDownloader.Application.Common.DTOs;
using VideoDownloader.Domain.Errors;

namespace VideoDownloader.Application.Downloads.Commands.RequestDownload;

public sealed record RequestDownloadCommand(string Url, string Format, string Quality, string? Title = null, string? ThumbnailUrl = null, string? Duration = null)
    : IRequest<Result<RequestDownloadResponse>>;
