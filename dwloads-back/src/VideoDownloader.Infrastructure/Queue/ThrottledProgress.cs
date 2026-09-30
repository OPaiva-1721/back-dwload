namespace VideoDownloader.Infrastructure.Queue;

/// <summary>
/// Synchronous, rate-limited progress sink. Unlike <see cref="Progress{T}"/>, which posts every
/// report to the thread pool (so callbacks can run out of order), this invokes the callback inline,
/// preserving order. Reports within <paramref name="interval"/> of the last emitted one are dropped,
/// except a report of 99+ which always goes through so the client sees the download finish.
/// </summary>
public sealed class ThrottledProgress(Action<int> onReport, TimeSpan interval, TimeProvider? time = null)
    : IProgress<int>
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private long _lastEmit = long.MinValue;
    private int _lastValue = -1;

    public void Report(int value)
    {
        if (value <= _lastValue) return;

        var now = _time.GetTimestamp();
        if (value < 99 && _lastEmit != long.MinValue && _time.GetElapsedTime(_lastEmit, now) < interval)
            return;

        _lastEmit = now;
        _lastValue = value;
        onReport(value);
    }
}
