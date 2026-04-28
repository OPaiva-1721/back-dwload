using MediatR;
using VideoDownloader.Application.Common.DTOs;
using VideoDownloader.Domain.Errors;

namespace VideoDownloader.Application.Downloads.Queries.GetVideoMetadata;

public sealed record GetVideoMetadataQuery(string Url)
    : IRequest<Result<VideoMetadataResponse>>;
