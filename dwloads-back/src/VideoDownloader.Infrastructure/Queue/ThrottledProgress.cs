using VideoDownloader.Application.Common.DTOs;

namespace VideoDownloader.Infrastructure.Queue;

/// <summary>
/// Synchronous, rate-limited progress sink. Unlike <see cref="Progress{T}"/>, which posts every
/// report to the thread pool (so callbacks can run out of order), this invokes the callback inline,
/// preserving order. Reports within <paramref name="interval"/> of the last emitted one are dropped,
/// except a step change or a report of 99+, which always go through so the client sees each phase.
/// </summary>
public sealed class ThrottledProgress(Action<DownloadProgress> onReport, TimeSpan interval, TimeProvider? time = null)
    : IProgress<DownloadProgress>
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private long _lastEmit = long.MinValue;
    private DownloadProgress? _last;

    public void Report(DownloadProgress value)
    {
        var stepChanged = _last is null || value.Step != _last.Step;
        if (!stepChanged && value.Percent <= _last!.Percent) return;

        var now = _time.GetTimestamp();
        if (!stepChanged && value.Percent < 99 && _time.GetElapsedTime(_lastEmit, now) < interval)
            return;

        _lastEmit = now;
        _last = value;
        onReport(value);
    }
}
