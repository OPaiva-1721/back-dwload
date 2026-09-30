using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VideoDownloader.Application.Common.DTOs;
using VideoDownloader.Application.Common.Interfaces;
using VideoDownloader.Domain.Enums;
using VideoDownloader.Domain.Errors;
using VideoDownloader.Domain.ValueObjects;
using VideoDownloader.Infrastructure.Queue;
using VideoDownloader.Infrastructure.Storage;

namespace VideoDownloader.Infrastructure.YtDlp;

public sealed class YtDlpVideoService(
    IOptions<YtDlpOptions> options,
    IOptions<StorageOptions> storageOptions,
    YtDlpConcurrencyLimiter limiter,
    IMemoryCache cache,
    ILogger<YtDlpVideoService> logger)
    : IVideoMetadataService, IVideoDownloadService
{
    // Extraction takes ~4s per call. Cached so the download can skip it via --load-info-json.
    // Short TTL: YouTube's signed stream URLs inside the info JSON expire after a few hours.
    internal static readonly TimeSpan MetadataTtl = TimeSpan.FromMinutes(10);

    private readonly string _ytDlpPath = options.Value.ExecutablePath;
    private readonly string? _cookiesFile = options.Value.CookiesFile;
    private readonly string _workPath = storageOptions.Value.WorkPath;

    private sealed record CachedMetadata(VideoMetadataResponse Response, string InfoJsonPath);

    private static string CacheKey(VideoUrl url) => $"ytdlp:meta:{url.Value}";

    public async Task<Result<VideoMetadataResponse>> GetMetadataAsync(VideoUrl url, CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey(url), out CachedMetadata? cached) && File.Exists(cached!.InfoJsonPath))
            return Result<VideoMetadataResponse>.Success(cached.Response);

        try
        {
            var json = await RunAsync([..BaseArgs(), "--dump-json", "--no-download", "--no-playlist", url.Value], ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var formats = root.TryGetProperty("formats", out var fmts)
                ? fmts.EnumerateArray().Where(f => !IsStoryboard(f)).Select(ToFormatInfo).ToList()
                : [];

            var response = new VideoMetadataResponse(
                root.GetProperty("title").GetString()!,
                root.TryGetProperty("thumbnail", out var thumb) ? thumb.GetString()! : string.Empty,
                TimeSpan.FromSeconds(
                    root.TryGetProperty("duration", out var dur) && dur.ValueKind == JsonValueKind.Number ? dur.GetDouble() : 0),
                formats);

            await CacheMetadataAsync(url, response, json, ct);
            return Result<VideoMetadataResponse>.Success(response);
        }
        catch (Exception ex)
        {
            // Raw yt-dlp output stays in the logs; users get a message they can act on
            logger.LogError(ex, "Failed to fetch metadata for {Url}", url.Value);
            return Result<VideoMetadataResponse>.Failure(ClassifyFailure(ex.Message) ?? MetadataFailed);
        }
    }

    public async Task<Result<string>> DownloadAsync(
        Guid jobId, VideoUrl url, DownloadFormat format, string quality,
        IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        await limiter.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(_workPath);
            var outputTemplate = Path.Combine(_workPath, $"{jobId}.%(ext)s");

            // Reuse the info JSON from a recent metadata call: skips re-extraction (~3s).
            // Copied per job so cache eviction can't delete it while yt-dlp is reading it.
            var jobInfoJson = Path.Combine(_workPath, $"src-{jobId}.json");
            string[] source = ["--no-playlist", url.Value];
            if (cache.TryGetValue(CacheKey(url), out CachedMetadata? cached))
            {
                try
                {
                    File.Copy(cached!.InfoJsonPath, jobInfoJson, overwrite: true);
                    source = ["--load-info-json", jobInfoJson];
                    logger.LogInformation("Job {JobId}: reusing cached metadata, skipping extraction", jobId);
                }
                catch (IOException) { /* evicted meanwhile — fall back to a fresh extraction */ }
            }

            string[] args = [
                ..BaseArgs(),
                "--newline",
                "--concurrent-fragments", "4",
                "--write-info-json",
                ..(format == DownloadFormat.Mp3
                    ? BuildAudioArgs(quality, outputTemplate)
                    : BuildVideoArgs(quality, outputTemplate)),
                ..source
            ];

            try
            {
                // Video = separate video + audio streams; audio-only = one stream
                var parser = new YtDlpProgressParser(expectedStreams: format == DownloadFormat.Mp3 ? 1 : 2);
                await RunWithProgressAsync(args, parser, progress, ct);

                // Exclude .info.json from the result — find the actual media file
                var finalPath = Directory
                    .GetFiles(_workPath, $"{jobId}.*")
                    .FirstOrDefault(f => !f.EndsWith(".info.json", StringComparison.OrdinalIgnoreCase));

                return finalPath is null
                    ? Result<string>.Failure(DownloadFailed)
                    : Result<string>.Success(finalPath);
            }
            catch (OperationCanceledException)
            {
                return Result<string>.Failure(new Error("YtDlp.Cancelled", "Download was cancelled."));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Download failed for job {JobId}", jobId);
                return Result<string>.Failure(ClassifyFailure(ex.Message) ?? DownloadFailed);
            }
            finally
            {
                try { File.Delete(jobInfoJson); } catch (IOException) { /* startup cleanup catches it */ }
            }
        }
        finally
        {
            limiter.Release();
        }
    }

    private static readonly Error MetadataFailed = new("YtDlp.MetadataFailed",
        "We couldn't read this video. Check the link and try again.");
    private static readonly Error DownloadFailed = new("YtDlp.DownloadFailed",
        "The download failed. Please try again in a moment.");

    // Order matters: the age check must win over the generic "Sign in to confirm" bot check
    private static readonly (string[] Needles, Error Error)[] KnownFailures =
    [
        (["confirm your age", "age-restricted", "inappropriate for some users"],
            new("YtDlp.AgeRestricted", "This video is age-restricted and can't be downloaded.")),
        (["Private video", "This video is private"],
            new("YtDlp.Private", "This video is private.")),
        (["not available in your country", "geo restriction", "geo-restricted", "blocked it in your country"],
            new("YtDlp.GeoBlocked", "This video isn't available in our region.")),
        (["live event will begin", "is live", "Premieres in", "is upcoming"],
            new("YtDlp.Live", "Live streams and premieres can be downloaded once they've ended.")),
        (["Video unavailable", "This video is unavailable", "has been removed", "does not exist", "HTTP Error 404"],
            new("YtDlp.Unavailable", "This video is unavailable. It may have been removed, or the link is wrong.")),
        (["Unsupported URL", "No video formats found", "no video in this post"],
            new("YtDlp.NoMedia", "We couldn't find a video at this link.")),
        (["Sign in to confirm", "cookies are no longer valid", "HTTP Error 403", "HTTP Error 429", "rate-limit"],
            new("YtDlp.Blocked", "The platform is temporarily blocking downloads. Please try again in a few minutes.")),
        (["Requested format is not available"],
            new("YtDlp.FormatUnavailable", "This quality isn't available for this video. Try a lower one.")),
    ];

    /// <summary>Maps well-known yt-dlp failures to user-facing errors; null when unrecognised.</summary>
    internal static Error? ClassifyFailure(string message)
    {
        foreach (var (needles, error) in KnownFailures)
            if (needles.Any(n => message.Contains(n, StringComparison.OrdinalIgnoreCase)))
                return error;
        return null;
    }

    private static bool IsStoryboard(JsonElement f) =>
        (f.TryGetProperty("format_note", out var note) && note.ValueKind == JsonValueKind.String && note.GetString() == "storyboard")
        || (f.TryGetProperty("ext", out var ext) && ext.ValueKind == JsonValueKind.String && ext.GetString() == "mhtml");

    internal static FormatInfo ToFormatInfo(JsonElement f)
    {
        static long? Long(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (long)v.GetDouble() : null;
        static double? Double(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
        // yt-dlp uses "none" for an absent stream; a missing key means unknown
        static bool Has(JsonElement e, string codec) =>
            e.TryGetProperty(codec, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() != "none";

        return new FormatInfo(
            f.GetProperty("format_id").GetString()!,
            f.TryGetProperty("ext", out var ext) ? ext.GetString() ?? "" : "",
            f.TryGetProperty("quality", out var q) ? q.ToString() : "unknown",
            Long(f, "filesize") ?? Long(f, "filesize_approx"),
            (int?)Long(f, "height"),
            HasVideo: Has(f, "vcodec"),
            HasAudio: Has(f, "acodec"),
            AudioBitrateKbps: Has(f, "acodec") ? Double(f, "abr") : null);
    }

    private string[] BaseArgs() =>
    [
        // deno is on by default; each extra runtime needs its own flag ("deno,node" is silently ignored)
        "--js-runtimes", "node",
        ..(!string.IsNullOrWhiteSpace(_cookiesFile) && File.Exists(_cookiesFile)
            ? (string[])["--cookies", _cookiesFile!]
            : [])
    ];

    private async Task CacheMetadataAsync(VideoUrl url, VideoMetadataResponse response, string json, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_workPath);
            // Unique file per entry, so evicting an old entry can never delete a newer entry's file
            var path = Path.Combine(_workPath, $"meta-{Guid.NewGuid():N}.info.json");
            await File.WriteAllTextAsync(path, json, ct);

            var entryOptions = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = MetadataTtl };
            entryOptions.RegisterPostEvictionCallback((_, value, _, _) =>
            {
                if (value is CachedMetadata old)
                    try { File.Delete(old.InfoJsonPath); } catch { /* best effort; startup cleanup catches leftovers */ }
            });
            cache.Set(CacheKey(url), new CachedMetadata(response, path), entryOptions);
        }
        catch (Exception ex)
        {
            // Caching is an optimisation — never fail the metadata request because of it
            logger.LogWarning(ex, "Failed to cache metadata for {Url}", url.Value);
        }
    }

    /// <summary>Best stream at or below the requested height ("720p"), preferring mp4/m4a.</summary>
    internal static string VideoFormatSelector(string quality) =>
        int.TryParse(quality.TrimEnd('p'), out var height) && height > 0
            ? $"bestvideo[height<={height}][ext=mp4]+bestaudio[ext=m4a]/bestvideo[height<={height}]+bestaudio/best[height<={height}]/best"
            : "bestvideo[ext=mp4]+bestaudio[ext=m4a]/bestvideo+bestaudio/best";

    private static string[] BuildVideoArgs(string quality, string outputTemplate) =>
        ["-f", VideoFormatSelector(quality), "--merge-output-format", "mp4", "-o", outputTemplate];

    private static string[] BuildAudioArgs(string quality, string outputTemplate)
    {
        var audioQuality = quality switch
        {
            "320kbps" => "320K",
            "256kbps" => "256K",
            "192kbps" => "192K",
            "128kbps" => "128K",
            _         => "0",
        };
        return ["-x", "--audio-format", "mp3", "--audio-quality", audioQuality, "-o", outputTemplate];
    }

    private async Task<string> RunAsync(string[] args, CancellationToken ct)
    {
        using var process = CreateProcess(args);
        process.Start();

        // Read both streams concurrently — never wait for one while the other fills its pipe buffer
        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            throw new InvalidOperationException(
                $"yt-dlp exited with code {process.ExitCode}. stderr: {error}");

        return output;
    }

    private async Task RunWithProgressAsync(
        string[] args, YtDlpProgressParser parser, IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        using var process = CreateProcess(args);
        var stderrBuilder = new StringBuilder();

        // Raised sequentially by a single reader, so the parser needs no locking
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null && parser.Parse(e.Data) is { } pct)
                progress.Report(pct);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stderrBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        if (process.ExitCode != 0)
        {
            var stderr = stderrBuilder.ToString().Trim();
            throw new InvalidOperationException(
                string.IsNullOrEmpty(stderr)
                    ? $"yt-dlp exited with code {process.ExitCode}."
                    : $"yt-dlp exited with code {process.ExitCode}. stderr: {stderr}");
        }
    }

    private Process CreateProcess(string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ytDlpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // ArgumentList handles escaping correctly — never concatenate args manually
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        return new Process { StartInfo = psi };
    }
}
