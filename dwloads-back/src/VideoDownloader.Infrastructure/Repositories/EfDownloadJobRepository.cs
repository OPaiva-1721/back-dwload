using Microsoft.EntityFrameworkCore;
using VideoDownloader.Domain.Entities;
using VideoDownloader.Domain.Interfaces;
using VideoDownloader.Infrastructure.Persistence;

namespace VideoDownloader.Infrastructure.Repositories;

public sealed class EfDownloadJobRepository(AppDbContext ctx) : IDownloadJobRepository
{
    public async Task<DownloadJob?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await ctx.DownloadJobs.FindAsync([id], ct);

    public async Task AddAsync(DownloadJob job, CancellationToken ct = default)
    {
        await ctx.DownloadJobs.AddAsync(job, ct);
        await ctx.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(DownloadJob job, CancellationToken ct = default)
    {
        ctx.DownloadJobs.Update(job);
        await ctx.SaveChangesAsync(ct);
    }
}
