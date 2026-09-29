using FluentAssertions;
using VideoDownloader.Domain.Entities;
using VideoDownloader.Domain.Enums;
using VideoDownloader.Domain.ValueObjects;

namespace VideoDownloader.Domain.Tests;

public sealed class DownloadJobTests
{
    private static DownloadJob CreateJob(DownloadFormat format = DownloadFormat.Mp4)
    {
        var url = VideoUrl.Create("https://youtube.com/watch?v=abc").Value!;
        return DownloadJob.Create(url, format, "720p");
    }

    [Fact]
    public void Create_SetsStatusToQueued()
    {
        var job = CreateJob();

        job.Status.Should().Be(DownloadStatus.Queued);
    }

    [Fact]
    public void Create_SetsNonEmptyId()
    {
        var job = CreateJob();

        job.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_SetsCreatedAtToUtcNow()
    {
        var before = DateTimeOffset.UtcNow;
        var job = CreateJob();
        var after = DateTimeOffset.UtcNow;

        job.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void Create_SetsProgressToZero()
    {
        var job = CreateJob();

        job.ProgressPercent.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void UpdateProgress_WithValidPercent_UpdatesProgress(int percent)
    {
        var job = CreateJob();

        job.UpdateProgress(percent);

        job.ProgressPercent.Should().Be(percent);
    }

    [Fact]
    public void UpdateProgress_SetsStatusToProcessing()
    {
        var job = CreateJob();

        job.UpdateProgress(42);

        job.Status.Should().Be(DownloadStatus.Processing);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void UpdateProgress_WithOutOfRangeValue_ThrowsArgumentOutOfRangeException(int percent)
    {
        var job = CreateJob();

        var act = () => job.UpdateProgress(percent);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Complete_SetsStatusToCompleted()
    {
        var job = CreateJob();

        job.Complete("/files/output.mp4");

        job.Status.Should().Be(DownloadStatus.Completed);
    }

    [Fact]
    public void Complete_SetsOutputFilePath()
    {
        var job = CreateJob();
        const string path = "/files/output.mp4";

        job.Complete(path);

        job.OutputFilePath.Should().Be(path);
    }

    [Fact]
    public void Complete_SetsProgressTo100()
    {
        var job = CreateJob();

        job.Complete("/files/output.mp4");

        job.ProgressPercent.Should().Be(100);
    }

    [Fact]
    public void Complete_SetsCompletedAt()
    {
        var before = DateTimeOffset.UtcNow;
        var job = CreateJob();

        job.Complete("/files/output.mp4");

        job.CompletedAt.Should().NotBeNull();
        job.CompletedAt!.Value.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void Fail_SetsStatusToFailed()
    {
        var job = CreateJob();

        job.Fail("yt-dlp error");

        job.Status.Should().Be(DownloadStatus.Failed);
    }

    [Fact]
    public void Fail_SetsErrorMessage()
    {
        var job = CreateJob();
        const string reason = "Network timeout";

        job.Fail(reason);

        job.ErrorMessage.Should().Be(reason);
    }

    [Fact]
    public void Fail_SetsCompletedAt()
    {
        var before = DateTimeOffset.UtcNow;
        var job = CreateJob();

        job.Fail("error");

        job.CompletedAt.Should().NotBeNull();
        job.CompletedAt!.Value.Should().BeOnOrAfter(before);
    }
}
