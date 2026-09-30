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
    JobCancellationRegistry cancellations,
    IOptions<StorageOptions> storageOptions,
    ILogger<DownloadJobProcessor> logger)
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(300);

    public async Task ProcessAsync(Guid jobId, CancellationToken ct)
    {
        // Registered before the job is loaded, so a cancel request can't slip in between
        using var registration = cancellations.Register(jobId, ct);

        var job = await repository.GetByIdAsync(jobId, ct);
        if (job is null)
        {
            logger.LogWarning("Job {JobId} not found", jobId);
            return;
        }

        if (job.IsFinished)
        {
            // Cancelled while still queued
            logger.LogInformation("Job {JobId} is {Status}, skipping", jobId, job.Status);
            return;
        }

        job.UpdateProgress(0);
        await repository.UpdateAsync(job, ct);

        // Live progress goes to SSE only. Persisting it would mean a DB write per yt-dlp line on a
        // DbContext that is not thread-safe, racing the final Complete() save. The notifier writes to a
        // DropOldest bounded channel, so the task completes synchronously and order is preserved.
        var progress = new ThrottledProgress(p =>
        {
            _ = notifier.NotifyProgressAsync(jobId, p, ct)
                .ContinueWith(t => logger.LogWarning(t.Exception, "Progress SSE notify failed for {JobId}", jobId),
                    TaskContinuationOptions.OnlyOnFaulted);
        }, ProgressInterval);

        var result = await downloadService.DownloadAsync(
            jobId, job.Url, job.Format, job.Quality, progress, registration.Token);

        if (registration.CancelledByUser(ct))
        {
            CleanTempFiles(jobId);
            job.Cancel();
            await repository.UpdateAsync(job, ct);
            await notifier.NotifyCancelledAsync(jobId, ct);
            return;
        }

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
            savedPath = await storage.ImportAsync(tempPath, Path.GetFileName(tempPath), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save downloaded file for job {JobId}", jobId);
            CleanTempFiles(jobId);
            job.Fail("Something went wrong while saving your file. Please try again.");
            await repository.UpdateAsync(job, ct);
            await notifier.NotifyFailedAsync(jobId, job.ErrorMessage!, ct);
            return;
        }
        finally
        {
            // After a successful move this is a no-op; on failure it removes the leftover
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        var (title, thumbnailUrl, duration) = !string.IsNullOrWhiteSpace(job.Title)
            ? (job.Title, job.ThumbnailUrl, job.Duration)
            : ReadAndDeleteInfoJson(jobId);

        // yt-dlp always writes {jobId}.info.json; when the client supplied the title it was never read
        CleanTempFiles(jobId);

        job.Complete(savedPath, title, thumbnailUrl, duration, fileSizeBytes);
        await repository.UpdateAsync(job, ct);

        var retention = storageOptions.Value.FileRetention;
        var downloadUrl = storage.GetDownloadUrl(savedPath, title);
        var payload = new DownloadCompletedPayload(
            DownloadUrl: downloadUrl,
            Title: title,
            ThumbnailUrl: thumbnailUrl,
            Duration: duration,
            Size: FileSizeFormatter.Format(fileSizeBytes),
            ExpiresAt: GetExpiresAt(),
            ExpiresAtUtc: job.CompletedAt!.Value + retention);

        await notifier.NotifyCompletedAsync(jobId, payload, ct);

        BackgroundJob.Schedule<FileCleanupJob>(
            j => j.DeleteAsync(savedPath, CancellationToken.None),
            retention);
    }

    private void CleanTempFiles(Guid jobId)
    {
        var workPath = storageOptions.Value.WorkPath;
        if (!Directory.Exists(workPath)) return;
        foreach (var file in Directory.GetFiles(workPath, $"{jobId}*"))
        {
            try { File.Delete(file); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to clean temp file {File}", file); }
        }
    }

    private (string title, string thumbnailUrl, string duration) ReadAndDeleteInfoJson(Guid jobId)
    {
        var workPath = storageOptions.Value.WorkPath;
        if (!Directory.Exists(workPath)) return (string.Empty, string.Empty, string.Empty);
        var infoFile = Directory.GetFiles(workPath, $"{jobId}*.info.json").FirstOrDefault();
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


    private static string FormatDuration(TimeSpan d) =>
        d.TotalHours >= 1
            ? $"{(int)d.TotalHours}:{d.Minutes:D2}:{d.Seconds:D2}"
            : $"{d.Minutes}:{d.Seconds:D2}";
}
