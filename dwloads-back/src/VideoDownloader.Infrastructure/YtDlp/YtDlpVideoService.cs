using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VideoDownloader.Application.Common.DTOs;
using VideoDownloader.Application.Common.Interfaces;
using VideoDownloader.Domain.Enums;
using VideoDownloader.Domain.Errors;
using VideoDownloader.Domain.ValueObjects;

namespace VideoDownloader.Infrastructure.YtDlp;

public sealed partial class YtDlpVideoService(
    IOptions<YtDlpOptions> options,
    YtDlpConcurrencyLimiter limiter,
    ILogger<YtDlpVideoService> logger)
    : IVideoMetadataService, IVideoDownloadService
{
    private readonly string _ytDlpPath = options.Value.ExecutablePath;
    private readonly string? _cookiesFile = options.Value.CookiesFile;

    // Compiled once — called thousands of times per download
    [GeneratedRegex(@"\[download\]\s+(\d+\.?\d*)%", RegexOptions.NonBacktracking)]
    private static partial Regex ProgressRegex();

    public async Task<Result<VideoMetadataResponse>> GetMetadataAsync(VideoUrl url, CancellationToken ct)
    {
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

            return Result<VideoMetadataResponse>.Success(new VideoMetadataResponse(
                root.GetProperty("title").GetString()!,
                root.TryGetProperty("thumbnail", out var thumb) ? thumb.GetString()! : string.Empty,
                TimeSpan.FromSeconds(root.GetProperty("duration").GetDouble()),
                formats));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch metadata for {Url}", url.Value);
            return Result<VideoMetadataResponse>.Failure(
                new Error("YtDlp.MetadataFailed", "Could not retrieve video metadata."));
        }
    }

    public async Task<Result<string>> DownloadAsync(
        Guid jobId, VideoUrl url, DownloadFormat format, string quality,
        IProgress<int> progress, CancellationToken ct)
    {
        await limiter.WaitAsync(ct);
        try
        {
            var outputTemplate = Path.Combine(Path.GetTempPath(), $"{jobId}.%(ext)s");
            string[] args = [
                ..BaseArgs(),
                "--no-playlist",
                "--write-info-json",
                ..(format == DownloadFormat.Mp3
                    ? BuildAudioArgs(quality, outputTemplate, url.Value)
                    : BuildVideoArgs(quality, outputTemplate, url.Value))
            ];

            try
            {
                await RunWithProgressAsync(args, progress, ct);

                // Exclude .info.json from the result — find the actual media file
                var finalPath = Directory
                    .GetFiles(Path.GetTempPath(), $"{jobId}.*")
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
                return Result<string>.Failure(new Error("YtDlp.DownloadFailed", ex.Message));
            }
        }
        finally
        {
            limiter.Release();
        }
    }

    private string[] BaseArgs() =>
    [
        "--js-runtimes", "deno,node",
        ..(!string.IsNullOrWhiteSpace(_cookiesFile) && File.Exists(_cookiesFile)
            ? (string[])["--cookies", _cookiesFile!]
            : [])
    ];

    private static string[] BuildVideoArgs(string quality, string outputTemplate, string url)
    {
        var formatSelector = quality switch
        {
            "2160p" => "bestvideo[height<=2160][ext=mp4]+bestaudio[ext=m4a]/bestvideo[height<=2160]+bestaudio/best",
            "1080p" => "bestvideo[height<=1080][ext=mp4]+bestaudio[ext=m4a]/bestvideo[height<=1080]+bestaudio/best",
            "720p"  => "bestvideo[height<=720][ext=mp4]+bestaudio[ext=m4a]/bestvideo[height<=720]+bestaudio/best",
            "480p"  => "bestvideo[height<=480][ext=mp4]+bestaudio[ext=m4a]/bestvideo[height<=480]+bestaudio/best",
            _       => "bestvideo[ext=mp4]+bestaudio[ext=m4a]/mp4",
        };
        return ["-f", formatSelector, "--merge-output-format", "mp4", "-o", outputTemplate, url];
    }

    private static string[] BuildAudioArgs(string quality, string outputTemplate, string url)
    {
        var audioQuality = quality switch
        {
            "320kbps" => "320K",
            "256kbps" => "256K",
            "192kbps" => "192K",
            "128kbps" => "128K",
            _         => "0",
        };
        return ["-x", "--audio-format", "mp3", "--audio-quality", audioQuality, "-o", outputTemplate, url];
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

    private async Task RunWithProgressAsync(string[] args, IProgress<int> progress, CancellationToken ct)
    {
        using var process = CreateProcess(args);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            var match = ProgressRegex().Match(e.Data);
            if (match.Success && double.TryParse(
                    match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var pct))
            {
                progress.Report((int)pct);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine(); // MUST consume stderr — pipe buffer deadlock otherwise

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
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
