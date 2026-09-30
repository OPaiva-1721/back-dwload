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

            var formats = root.GetProperty("formats").EnumerateArray()
                .Select(f => new FormatInfo(
                    f.GetProperty("format_id").GetString()!,
                    f.GetProperty("ext").GetString()!,
                    f.TryGetProperty("quality", out var q) ? q.ToString() : "unknown",
                    f.TryGetProperty("filesize", out var fs) && fs.ValueKind == JsonValueKind.Number
                        ? fs.GetInt64() : null,
                    f.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number
                        ? h.GetInt32() : null))
                .ToList();

            var response = new VideoMetadataResponse(
                root.GetProperty("title").GetString()!,
                root.TryGetProperty("thumbnail", out var thumb) ? thumb.GetString()! : string.Empty,
                TimeSpan.FromSeconds(root.GetProperty("duration").GetDouble()),
                formats);

            await CacheMetadataAsync(url, response, json, ct);
            return Result<VideoMetadataResponse>.Success(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch metadata for {Url}", url.Value);
            if (ClassifyFailure(ex.Message) is { } known)
                return Result<VideoMetadataResponse>.Failure(known);
            var detail = ex.Message.Length > 300 ? ex.Message[..300] : ex.Message;
            return Result<VideoMetadataResponse>.Failure(
                new Error("YtDlp.MetadataFailed", $"Could not retrieve video metadata. {detail}"));
        }
    }

    public async Task<Result<string>> DownloadAsync(
        Guid jobId, VideoUrl url, DownloadFormat format, string quality,
        IProgress<int> progress, CancellationToken ct)
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
                    ? Result<string>.Failure(new Error("YtDlp.OutputNotFound", "Download output file not found."))
                    : Result<string>.Success(finalPath);
            }
            catch (OperationCanceledException)
            {
                return Result<string>.Failure(new Error("YtDlp.Cancelled", "Download was cancelled."));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Download failed for job {JobId}", jobId);
                return Result<string>.Failure(
                    ClassifyFailure(ex.Message) ?? new Error("YtDlp.DownloadFailed", ex.Message));
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

    /// <summary>Maps well-known yt-dlp failures to actionable errors; null when unrecognised.</summary>
    internal static Error? ClassifyFailure(string message)
    {
        if (message.Contains("Sign in to confirm", StringComparison.OrdinalIgnoreCase)
            || message.Contains("cookies are no longer valid", StringComparison.OrdinalIgnoreCase))
            return new Error("YtDlp.AuthRequired",
                "YouTube session expired or was flagged as a bot. Regenerate cookies.txt (tools/yt_cookies: login, then export).");

        if (message.Contains("Requested format is not available", StringComparison.OrdinalIgnoreCase))
            return new Error("YtDlp.FormatUnavailable",
                "Requested format is not available. Check that deno/node and ffmpeg are installed and yt-dlp is up to date.");

        return null;
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

    private static string[] BuildVideoArgs(string quality, string outputTemplate)
    {
        var formatSelector = quality switch
        {
            "2160p" => "bestvideo[height<=2160][ext=mp4]+bestaudio[ext=m4a]/bestvideo[height<=2160]+bestaudio/best",
            "1080p" => "bestvideo[height<=1080][ext=mp4]+bestaudio[ext=m4a]/bestvideo[height<=1080]+bestaudio/best",
            "720p"  => "bestvideo[height<=720][ext=mp4]+bestaudio[ext=m4a]/bestvideo[height<=720]+bestaudio/best",
            "480p"  => "bestvideo[height<=480][ext=mp4]+bestaudio[ext=m4a]/bestvideo[height<=480]+bestaudio/best",
            _       => "bestvideo[ext=mp4]+bestaudio[ext=m4a]/mp4",
        };
        return ["-f", formatSelector, "--merge-output-format", "mp4", "-o", outputTemplate];
    }

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
        string[] args, YtDlpProgressParser parser, IProgress<int> progress, CancellationToken ct)
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
