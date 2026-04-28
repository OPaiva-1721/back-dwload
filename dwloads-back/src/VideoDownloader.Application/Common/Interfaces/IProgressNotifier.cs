using VideoDownloader.Application.Common.DTOs;

namespace VideoDownloader.Application.Common.Interfaces;

public interface IProgressNotifier
{
    Task NotifyProgressAsync(Guid jobId, int percent, CancellationToken ct = default);
    Task NotifyCompletedAsync(Guid jobId, DownloadCompletedPayload payload, CancellationToken ct = default);
    Task NotifyFailedAsync(Guid jobId, string reason, CancellationToken ct = default);
}
