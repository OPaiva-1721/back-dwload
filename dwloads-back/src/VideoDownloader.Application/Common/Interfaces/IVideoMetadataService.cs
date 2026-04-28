using VideoDownloader.Application.Common.DTOs;
using VideoDownloader.Domain.Errors;
using VideoDownloader.Domain.ValueObjects;

namespace VideoDownloader.Application.Common.Interfaces;

public interface IVideoMetadataService
{
    Task<Result<VideoMetadataResponse>> GetMetadataAsync(VideoUrl url, CancellationToken ct = default);
}
