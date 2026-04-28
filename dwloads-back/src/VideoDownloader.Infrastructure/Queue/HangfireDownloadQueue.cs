using Hangfire;
using VideoDownloader.Application.Common.Interfaces;

namespace VideoDownloader.Infrastructure.Queue;

public sealed class HangfireDownloadQueue : IDownloadQueue
{
    public Task EnqueueAsync(Guid jobId, CancellationToken ct)
    {
        BackgroundJob.Enqueue<DownloadJobProcessor>(p =>
            p.ProcessAsync(jobId, CancellationToken.None));
        return Task.CompletedTask;
    }
}
