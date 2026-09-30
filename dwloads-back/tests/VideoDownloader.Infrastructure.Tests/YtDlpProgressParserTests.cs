using FluentAssertions;
using VideoDownloader.Infrastructure.YtDlp;

namespace VideoDownloader.Infrastructure.Tests;

public sealed class YtDlpProgressParserTests
{
    [Fact]
    public void Parse_SingleStream_ReportsPercentAndCapsAt99()
    {
        var parser = new YtDlpProgressParser(expectedStreams: 1);
        parser.Parse("[download] Destination: /work/a.webm");

        parser.Parse("[download]  42.5% of 3.00MiB at 1.00MiB/s ETA 00:02").Should().Be(42);
        parser.Parse("[download] 100% of 3.00MiB in 00:00:03").Should().Be(99);
    }

    [Fact]
    public void Parse_VideoPlusAudio_IsMonotonicAcrossStreams()
    {
        var parser = new YtDlpProgressParser(expectedStreams: 2);
        var reported = new List<int>();
        string[] lines =
        [
            "[download] Destination: /work/a.f137.mp4",
            "[download]  50.0% of 10MiB", "[download] 100% of 10MiB",
            "[download] Destination: /work/a.f140.m4a",
            "[download]   0.0% of 2MiB", "[download]  50.0% of 2MiB", "[download] 100% of 2MiB",
        ];

        foreach (var line in lines)
            if (parser.Parse(line) is { } pct) reported.Add(pct);

        reported.Should().Equal(25, 50, 75, 99);
    }

    [Fact]
    public void Parse_IgnoresNonProgressAndBackwardValues()
    {
        var parser = new YtDlpProgressParser(expectedStreams: 1);

        parser.Parse("[youtube] abc: Downloading webpage").Should().BeNull();
        parser.Parse("[download]  30.0% of 1MiB").Should().Be(30);
        parser.Parse("[download]  30.0% of 1MiB").Should().BeNull();
        parser.Parse("[download]  10.0% of 1MiB").Should().BeNull();
    }

    [Fact]
    public void Parse_MoreStreamsThanExpected_NeverExceeds99()
    {
        var parser = new YtDlpProgressParser(expectedStreams: 1);
        parser.Parse("[download] Destination: /work/a.mp4");
        parser.Parse("[download] 100% of 1MiB");
        parser.Parse("[download] Destination: /work/b.mp4");

        parser.Parse("[download] 100% of 1MiB").Should().BeNull();
    }
}
