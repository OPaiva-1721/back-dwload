using FluentAssertions;
using VideoDownloader.Domain.Entities;
using VideoDownloader.Domain.Enums;
using VideoDownloader.Domain.ValueObjects;
using VideoDownloader.Infrastructure.Repositories;

namespace VideoDownloader.Infrastructure.Tests;

public sealed class InMemoryDownloadJobRepositoryTests
{
    private readonly InMemoryDownloadJobRepository _repository = new();

    [Fact]
    public async Task GetByIdAsync_WithNonExistingId_ReturnsNull()
    {
        var result = await _repository.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_ThenGetById_ReturnsJob()
    {
        var job = MakeJob();

        await _repository.AddAsync(job, CancellationToken.None);
        var found = await _repository.GetByIdAsync(job.Id, CancellationToken.None);

        found.Should().NotBeNull();
        found!.Id.Should().Be(job.Id);
    }

    [Fact]
    public async Task UpdateAsync_PersistsChanges()
    {
        var job = MakeJob();
        await _repository.AddAsync(job, CancellationToken.None);

        job.UpdateProgress(75);
        await _repository.UpdateAsync(job, CancellationToken.None);

        var updated = await _repository.GetByIdAsync(job.Id, CancellationToken.None);
        updated!.ProgressPercent.Should().Be(75);
        updated.Status.Should().Be(DownloadStatus.Processing);
    }

    [Fact]
    public async Task AddAsync_MultipleJobs_EachRetrievableById()
    {
        var job1 = MakeJob();
        var job2 = MakeJob();

        await _repository.AddAsync(job1, CancellationToken.None);
        await _repository.AddAsync(job2, CancellationToken.None);

        var found1 = await _repository.GetByIdAsync(job1.Id, CancellationToken.None);
        var found2 = await _repository.GetByIdAsync(job2.Id, CancellationToken.None);

        found1!.Id.Should().Be(job1.Id);
        found2!.Id.Should().Be(job2.Id);
    }

    private static DownloadJob MakeJob()
    {
        var url = VideoUrl.Create("https://youtube.com/watch?v=test").Value!;
        return DownloadJob.Create(url, DownloadFormat.Mp4, "720p");
    }
}
