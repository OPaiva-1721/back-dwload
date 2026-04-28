using System.Collections.Concurrent;
using VideoDownloader.Domain.Entities;
using VideoDownloader.Domain.Interfaces;

namespace VideoDownloader.Infrastructure.Repositories;

public sealed class InMemoryDownloadJobRepository : IDownloadJobRepository
{
    private readonly ConcurrentDictionary<Guid, DownloadJob> _store = new();

    public Task<DownloadJob?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_store.GetValueOrDefault(id));

    public Task AddAsync(DownloadJob job, CancellationToken ct)
    {
        _store[job.Id] = job;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(DownloadJob job, CancellationToken ct)
    {
        _store[job.Id] = job;
        return Task.CompletedTask;
    }
}
