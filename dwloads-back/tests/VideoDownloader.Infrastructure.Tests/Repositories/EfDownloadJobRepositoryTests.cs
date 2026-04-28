using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VideoDownloader.Domain.Entities;
using VideoDownloader.Domain.Enums;
using VideoDownloader.Domain.ValueObjects;
using VideoDownloader.Infrastructure.Persistence;
using VideoDownloader.Infrastructure.Repositories;

namespace VideoDownloader.Infrastructure.Tests.Repositories;

public sealed class EfDownloadJobRepositoryTests : IDisposable
{
    private readonly AppDbContext _ctx;
    private readonly EfDownloadJobRepository _repo;

    public EfDownloadJobRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _ctx = new AppDbContext(options);
        _repo = new EfDownloadJobRepository(_ctx);
    }

    [Fact]
    public async Task AddAsync_persists_job_and_GetByIdAsync_returns_it()
    {
        var url = VideoUrl.Create("https://www.youtube.com/watch?v=dQw4w9WgXcQ").Value!;
        var job = DownloadJob.Create(url, DownloadFormat.Mp4, "1080p", "Test Title");

        await _repo.AddAsync(job);

        var retrieved = await _repo.GetByIdAsync(job.Id);

        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(job.Id);
        retrieved.Title.Should().Be("Test Title");
        retrieved.Status.Should().Be(DownloadStatus.Queued);
    }

    [Fact]
    public async Task UpdateAsync_persists_status_change()
    {
        var url = VideoUrl.Create("https://www.youtube.com/watch?v=dQw4w9WgXcQ").Value!;
        var job = DownloadJob.Create(url, DownloadFormat.Mp4, "1080p");
        await _repo.AddAsync(job);

        job.UpdateProgress(50);
        await _repo.UpdateAsync(job);

        var retrieved = await _repo.GetByIdAsync(job.Id);
        retrieved!.ProgressPercent.Should().Be(50);
        retrieved.Status.Should().Be(DownloadStatus.Processing);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_for_unknown_id()
    {
        var result = await _repo.GetByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    public void Dispose() => _ctx.Dispose();
}
