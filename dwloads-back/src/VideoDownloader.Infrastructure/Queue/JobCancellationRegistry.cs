using System.Collections.Concurrent;
using VideoDownloader.Application.Common.Interfaces;

namespace VideoDownloader.Infrastructure.Queue;

/// <summary>
/// Tracks running downloads on this instance so a user can stop one. Single-instance by design:
/// a cancel request that lands on another instance falls back to marking the job cancelled.
/// </summary>
public sealed class JobCancellationRegistry : IDownloadCanceller
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    /// <summary>Registers a running job; dispose the result when the job ends.</summary>
    public Registration Register(Guid jobId, CancellationToken hostToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
        _running[jobId] = cts;
        return new Registration(this, jobId, cts);
    }

    public bool TryCancel(Guid jobId)
    {
        if (!_running.TryGetValue(jobId, out var cts)) return false;
        try { cts.Cancel(); }
        catch (ObjectDisposedException) { return false; }
        return true;
    }

    public sealed class Registration(JobCancellationRegistry owner, Guid jobId, CancellationTokenSource cts) : IDisposable
    {
        public CancellationToken Token => cts.Token;

        /// <summary>True when the user cancelled (as opposed to the host shutting down).</summary>
        public bool CancelledByUser(CancellationToken hostToken) => cts.IsCancellationRequested && !hostToken.IsCancellationRequested;

        public void Dispose()
        {
            owner._running.TryRemove(new KeyValuePair<Guid, CancellationTokenSource>(jobId, cts));
            cts.Dispose();
        }
    }
}
