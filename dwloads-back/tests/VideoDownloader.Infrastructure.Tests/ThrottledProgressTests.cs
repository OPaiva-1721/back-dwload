using FluentAssertions;
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
    private readonly List<int> _emitted = [];
    private readonly ThrottledProgress _progress;

    public ThrottledProgressTests() =>
        _progress = new ThrottledProgress(_emitted.Add, TimeSpan.FromMilliseconds(300), _time);

    [Fact]
    public void Report_WithinInterval_DropsIntermediateValues()
    {
        _progress.Report(1);
        _progress.Report(2);
        _progress.Report(3);
        _time.Advance(TimeSpan.FromMilliseconds(300));
        _progress.Report(4);

        _emitted.Should().Equal(1, 4);
    }

    [Fact]
    public void Report_FinalValue_AlwaysEmittedEvenWithinInterval()
    {
        _progress.Report(10);
        _progress.Report(99);

        _emitted.Should().Equal(10, 99);
    }

    [Fact]
    public void Report_NonIncreasingValues_AreIgnored()
    {
        _progress.Report(50);
        _time.Advance(TimeSpan.FromSeconds(1));
        _progress.Report(40);
        _progress.Report(50);

        _emitted.Should().Equal(50);
    }

    [Fact]
    public void Report_InvokesCallbackSynchronouslyInOrder()
    {
        for (var i = 0; i < 5; i++)
        {
            _progress.Report(i * 10);
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        _emitted.Should().Equal(0, 10, 20, 30, 40);
    }
}
