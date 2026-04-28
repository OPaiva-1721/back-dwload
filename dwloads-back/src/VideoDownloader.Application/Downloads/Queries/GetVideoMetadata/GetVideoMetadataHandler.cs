using MediatR;
using VideoDownloader.Application.Common.DTOs;
using VideoDownloader.Application.Common.Interfaces;
using VideoDownloader.Domain.Errors;
using VideoDownloader.Domain.ValueObjects;

namespace VideoDownloader.Application.Downloads.Queries.GetVideoMetadata;

public sealed class GetVideoMetadataHandler(IVideoMetadataService metadataService)
    : IRequestHandler<GetVideoMetadataQuery, Result<VideoMetadataResponse>>
{
    public async Task<Result<VideoMetadataResponse>> Handle(
        GetVideoMetadataQuery request, CancellationToken ct)
    {
        var urlResult = VideoUrl.Create(request.Url);
        if (urlResult.IsFailure)
            return Result<VideoMetadataResponse>.Failure(urlResult.Error);

        return await metadataService.GetMetadataAsync(urlResult.Value!, ct);
    }
}
