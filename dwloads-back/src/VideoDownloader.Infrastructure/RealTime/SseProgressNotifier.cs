using System.Text.Json;
using VideoDownloader.Application.Common.DTOs;
using VideoDownloader.Application.Common.Interfaces;

namespace VideoDownloader.Infrastructure.RealTime;

public sealed class SseProgressNotifier(SseConnectionManager manager) : IProgressNotifier
{
    public async Task NotifyProgressAsync(Guid jobId, int percent, CancellationToken ct)
    {
        var channel = manager.GetOrCreate(jobId);
        var data = JsonSerializer.Serialize(new { step = "fetching", percent });
        await channel.Writer.WriteAsync(FormatEvent("progress", data), ct);
    }

    public async Task NotifyCompletedAsync(Guid jobId, DownloadCompletedPayload payload, CancellationToken ct)
    {
        var channel = manager.GetOrCreate(jobId);
        var data = JsonSerializer.Serialize(new
        {
            downloadUrl = payload.DownloadUrl,
            title = payload.Title,
            thumbnail = payload.ThumbnailUrl,
            duration = payload.Duration,
            size = payload.Size,
            expiresAt = payload.ExpiresAt,
        });
        await channel.Writer.WriteAsync(FormatEvent("done", data), ct);
        manager.TryComplete(jobId);
    }

    public async Task NotifyFailedAsync(Guid jobId, string reason, CancellationToken ct)
    {
        var channel = manager.GetOrCreate(jobId);
        var data = JsonSerializer.Serialize(new { message = reason });
        await channel.Writer.WriteAsync(FormatEvent("failed", data), ct);
        manager.TryComplete(jobId);
    }

    private static string FormatEvent(string type, string data) =>
        $"event: {type}\ndata: {data}\n\n";
}
