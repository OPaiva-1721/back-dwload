using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using VideoDownloader.Application.Downloads.Queries.GetDownloadStatus;
using Microsoft.Extensions.Options;
using VideoDownloader.Infrastructure.RealTime;
using VideoDownloader.Infrastructure.Storage;

namespace VideoDownloader.Api.Endpoints;

public static class StreamEndpoints
{
    public static void MapStreamEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/downloads/{jobId:guid}/stream", async (
            Guid jobId,
            SseConnectionManager manager,
            ISender sender,
            IOptions<StorageOptions> storageOptions,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            // If the job is already in a terminal state, respond immediately
            var statusResult = await sender.Send(new GetDownloadStatusQuery(jobId), ct);
            if (statusResult.IsSuccess)
            {
                var status = statusResult.Value!;

                if (status.Status == "Completed" && status.DownloadUrl is not null)
                {
                    var expiresAtUtc = (status.CompletedAt ?? DateTimeOffset.UtcNow) + storageOptions.Value.FileRetention;
                    var data = JsonSerializer.Serialize(new
                    {
                        downloadUrl = status.DownloadUrl,
                        title = status.Title,
                        thumbnail = status.ThumbnailUrl,
                        duration = status.Duration,
                        size = status.FileSizeBytes > 0
                            ? FileSizeFormatter.Format(status.FileSizeBytes)
                            : string.Empty,
                        expiresAt = "in 1 hour",
                        expiresAtUtc,
                    });
                    await ctx.Response.WriteAsync($"event: done\ndata: {data}\n\n", ct);
                    return;
                }

                if (status.Status == "Cancelled")
                {
                    await ctx.Response.WriteAsync("event: cancelled\ndata: {}\n\n", ct);
                    return;
                }

                if (status.Status == "Failed")
                {
                    var data = JsonSerializer.Serialize(new
                    {
                        message = status.ErrorMessage ?? "Download failed"
                    });
                    await ctx.Response.WriteAsync($"event: failed\ndata: {data}\n\n", ct);
                    return;
                }
            }

            // Stream live events as they arrive
            var channel = manager.GetOrCreate(jobId);
            try
            {
                await foreach (var sseEvent in channel.Reader.ReadAllAsync(ct))
                {
                    await ctx.Response.WriteAsync(sseEvent, ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Client closed the tab or navigated away mid-download: normal, not an error
            }
        })
        .WithName("StreamDownloadEvents")
        .WithSummary("Stream download progress via SSE")
        .WithTags("Downloads");
    }
}
