using FluentAssertions;
using VideoDownloader.Application.Common.DTOs;
using VideoDownloader.Infrastructure.Queue;

namespace VideoDownloader.Infrastructure.Tests;

public sealed class ThrottledProgressTests
{
    private sealed class ManualTime : TimeProvider
    {
        public long Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
        public void Advance(TimeSpan by) => Ticks += by.Ticks;
    }

    private readonly ManualTime _time = new();
    private readonly List<DownloadProgress> _emitted = [];
    private readonly ThrottledProgress _progress;

    public ThrottledProgressTests() =>
        _progress = new ThrottledProgress(_emitted.Add, TimeSpan.FromMilliseconds(300), _time);

    private static DownloadProgress Dl(int pct) => new(DownloadStep.Downloading, pct);

    [Fact]
    public void Report_WithinInterval_DropsIntermediateValues()
    {
        _progress.Report(Dl(1));
        _progress.Report(Dl(2));
        _progress.Report(Dl(3));
        _time.Advance(TimeSpan.FromMilliseconds(300));
        _progress.Report(Dl(4));

        _emitted.Select(p => p.Percent).Should().Equal(1, 4);
    }

    [Fact]
    public void Report_FinalValue_AlwaysEmittedEvenWithinInterval()
    {
        _progress.Report(Dl(10));
        _progress.Report(Dl(99));

        _emitted.Select(p => p.Percent).Should().Equal(10, 99);
    }

    [Fact]
    public void Report_StepChange_AlwaysEmittedEvenWithinInterval()
    {
        _progress.Report(Dl(99));
        _progress.Report(new DownloadProgress(DownloadStep.Converting, 99));

        _emitted.Select(p => p.Step).Should().Equal(DownloadStep.Downloading, DownloadStep.Converting);
    }

    [Fact]
    public void Report_NonIncreasingValues_AreIgnored()
    {
        _progress.Report(Dl(50));
        _time.Advance(TimeSpan.FromSeconds(1));
        _progress.Report(Dl(40));
        _progress.Report(Dl(50));

        _emitted.Select(p => p.Percent).Should().Equal(50);
    }

    [Fact]
    public void Report_InvokesCallbackSynchronouslyInOrder()
    {
        for (var i = 0; i < 5; i++)
        {
            _progress.Report(Dl(i * 10));
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        _emitted.Select(p => p.Percent).Should().Equal(0, 10, 20, 30, 40);
    }
}
