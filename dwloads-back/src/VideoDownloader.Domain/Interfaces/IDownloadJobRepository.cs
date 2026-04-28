using VideoDownloader.Domain.Entities;

namespace VideoDownloader.Domain.Interfaces;

public interface IDownloadJobRepository
{
    Task<DownloadJob?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(DownloadJob job, CancellationToken ct = default);
    Task UpdateAsync(DownloadJob job, CancellationToken ct = default);
}
