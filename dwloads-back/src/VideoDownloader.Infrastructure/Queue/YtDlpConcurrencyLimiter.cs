namespace VideoDownloader.Infrastructure.Queue;

public sealed class YtDlpConcurrencyLimiter(int maxConcurrent = 2) : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(maxConcurrent, maxConcurrent);

    public Task WaitAsync(CancellationToken ct) => _semaphore.WaitAsync(ct);

    public void Release() => _semaphore.Release();

    public void Dispose() => _semaphore.Dispose();
}
