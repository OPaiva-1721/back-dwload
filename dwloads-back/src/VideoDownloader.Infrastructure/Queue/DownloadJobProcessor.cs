using System.Text.Json;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VideoDownloader.Application.Common.DTOs;
using VideoDownloader.Application.Common.Interfaces;
using VideoDownloader.Domain.Interfaces;
using VideoDownloader.Infrastructure.Storage;

namespace VideoDownloader.Infrastructure.Queue;

public sealed class DownloadJobProcessor(
    IDownloadJobRepository repository,
    IVideoDownloadService downloadService,
    IStorageService storage,
    IProgressNotifier notifier,
    IOptions<StorageOptions> storageOptions,
    ILogger<DownloadJobProcessor> logger)
{
    public async Task ProcessAsync(Guid jobId, CancellationToken ct)
    {
        var job = await repository.GetByIdAsync(jobId, ct);
        if (job is null)
        {
            logger.LogWarning("Job {JobId} not found", jobId);
            return;
        }

        // Progress<T> takes Action<T> — async lambda here would be async void (exceptions swallowed).
        // Both underlying calls are effectively synchronous (in-memory repo + bounded channel TryWrite),
        // so fire-and-forget with logged continuation is correct and safe.
        var progress = new Progress<int>(pct =>
        {
            job.UpdateProgress(pct);
            _ = repository.UpdateAsync(job, ct)
                .ContinueWith(t => logger.LogWarning(t.Exception, "Progress repo update failed for {JobId}", jobId),
                    TaskContinuationOptions.OnlyOnFaulted);
            _ = notifier.NotifyProgressAsync(jobId, pct, ct)
                .ContinueWith(t => logger.LogWarning(t.Exception, "Progress SSE notify failed for {JobId}", jobId),
                    TaskContinuationOptions.OnlyOnFaulted);
        });

        var result = await downloadService.DownloadAsync(jobId, job.Url, job.Format, job.Quality, progress, ct);

        if (result.IsFailure)
        {
            CleanTempFiles(jobId);
            job.Fail(result.Error.Message);
            await repository.UpdateAsync(job, ct);
            await notifier.NotifyFailedAsync(jobId, result.Error.Message, ct);
            return;
        }

        string tempPath = result.Value!;
        string savedPath;
        long fileSizeBytes;

        try
        {
            fileSizeBytes = new FileInfo(tempPath).Length;
            await using var fs = File.OpenRead(tempPath);
            savedPath = await storage.SaveAsync(fs, Path.GetFileName(tempPath), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save downloaded file for job {JobId}", jobId);
            CleanTempFiles(jobId);
            job.Fail("Failed to save the downloaded file.");
            await repository.UpdateAsync(job, ct);
            await notifier.NotifyFailedAsync(jobId, job.ErrorMessage!, ct);
            return;
        }
        finally
        {
            // Always remove the original temp file regardless of success or failure
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        var (title, thumbnailUrl, duration) = !string.IsNullOrWhiteSpace(job.Title)
            ? (job.Title, job.ThumbnailUrl, job.Duration)
            : ReadAndDeleteInfoJson(jobId);

        job.Complete(savedPath, title, thumbnailUrl, duration, fileSizeBytes);
        await repository.UpdateAsync(job, ct);

        var downloadUrl = storage.GetDownloadUrl(savedPath, title);
        var payload = new DownloadCompletedPayload(
            DownloadUrl: downloadUrl,
            Title: title,
            ThumbnailUrl: thumbnailUrl,
            Duration: duration,
            Size: FormatFileSize(fileSizeBytes),
            ExpiresAt: GetExpiresAt());

        await notifier.NotifyCompletedAsync(jobId, payload, ct);

        BackgroundJob.Schedule<FileCleanupJob>(
            j => j.DeleteAsync(savedPath, CancellationToken.None),
            storageOptions.Value.FileRetention);
    }

    private void CleanTempFiles(Guid jobId)
    {
        foreach (var file in Directory.GetFiles(Path.GetTempPath(), $"{jobId}*"))
        {
            try { File.Delete(file); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to clean temp file {File}", file); }
        }
    }

    private static (string title, string thumbnailUrl, string duration) ReadAndDeleteInfoJson(Guid jobId)
    {
        var infoFile = Directory.GetFiles(Path.GetTempPath(), $"{jobId}*.info.json").FirstOrDefault();
        if (infoFile is null) return (string.Empty, string.Empty, string.Empty);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(infoFile));
            var root = doc.RootElement;
            var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            var thumb = root.TryGetProperty("thumbnail", out var th) ? th.GetString() ?? string.Empty : string.Empty;
            var dur = root.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number
                ? FormatDuration(TimeSpan.FromSeconds(d.GetDouble())) : string.Empty;
            return (title, thumb, dur);
        }
        catch { return (string.Empty, string.Empty, string.Empty); }
        finally { File.Delete(infoFile); }
    }

    private string GetExpiresAt()
    {
        var h = (int)storageOptions.Value.FileRetention.TotalHours;
        return h == 1 ? "in 1 hour" : $"in {h} hours";
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
        return $"{bytes / 1024.0:F1} KB";
    }

    private static string FormatDuration(TimeSpan d) =>
        d.TotalHours >= 1
            ? $"{(int)d.TotalHours}:{d.Minutes:D2}:{d.Seconds:D2}"
            : $"{d.Minutes}:{d.Seconds:D2}";
}
