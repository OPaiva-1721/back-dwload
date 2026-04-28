namespace VideoDownloader.Application.Common.Interfaces;

public interface IDownloadQueue
{
    Task EnqueueAsync(Guid jobId, CancellationToken ct = default);
}
