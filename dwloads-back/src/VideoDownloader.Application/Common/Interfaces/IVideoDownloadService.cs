using VideoDownloader.Domain.Enums;
using VideoDownloader.Domain.Errors;
using VideoDownloader.Domain.ValueObjects;

namespace VideoDownloader.Application.Common.Interfaces;

public interface IVideoDownloadService
{
    Task<Result<string>> DownloadAsync(
        Guid jobId,
        VideoUrl url,
        DownloadFormat format,
        string quality,
        IProgress<int> progress,
        CancellationToken ct = default);
}
