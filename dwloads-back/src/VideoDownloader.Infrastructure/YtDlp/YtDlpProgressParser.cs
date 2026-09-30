using System.Globalization;
using System.Text.RegularExpressions;

namespace VideoDownloader.Infrastructure.YtDlp;

/// <summary>
/// Turns yt-dlp stdout lines into a single overall percentage.
/// yt-dlp downloads video and audio as separate streams, each reporting 0→100%,
/// so raw values jump back to 0 mid-download. Each "Destination:" line starts a new
/// stream; overall = (finished streams + current fraction) / expected streams.
/// Output is monotonic and capped at 99 — 100 is reserved for the completed job.
/// Not thread-safe: feed lines from a single reader.
/// </summary>
public sealed partial class YtDlpProgressParser(int expectedStreams)
{
    [GeneratedRegex(@"\[download\]\s+(\d+\.?\d*)%", RegexOptions.NonBacktracking)]
    private static partial Regex PercentRegex();

    private readonly int _expectedStreams = Math.Max(1, expectedStreams);
    private int _streamIndex = -1;
    private int _last = -1;

    /// <returns>New overall percent, or null when the line carries no progress or no advance.</returns>
    public int? Parse(string line)
    {
        if (line.StartsWith("[download] Destination:", StringComparison.Ordinal))
        {
            _streamIndex++;
            return null;
        }

        var match = PercentRegex().Match(line);
        if (!match.Success
            || !double.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var pct))
            return null;

        var finished = Math.Clamp(_streamIndex, 0, _expectedStreams - 1);
        var overall = (int)((finished * 100 + Math.Clamp(pct, 0, 100)) / _expectedStreams);
        overall = Math.Min(overall, 99);

        if (overall <= _last) return null;
        _last = overall;
        return overall;
    }
}
