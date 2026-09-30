using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VideoDownloader.Api.Contracts;
using VideoDownloader.Application.Common.DTOs;
using VideoDownloader.Application.Downloads.Commands.CancelDownload;
using VideoDownloader.Application.Downloads.Commands.RequestDownload;
using VideoDownloader.Application.Downloads.Queries.GetDownloadStatus;
using VideoDownloader.Domain.Errors;
using VideoDownloader.Infrastructure.Storage;

namespace VideoDownloader.Api.Endpoints;

public static class DownloadEndpoints
{
    public static void MapDownloadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/downloads", async (
            DownloadRequest request,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(
                new RequestDownloadCommand(request.Url, request.Format, request.Quality, request.Title, request.ThumbnailUrl, request.Duration), ct);

            return result.Match(
                onSuccess: r => Results.Accepted($"/api/downloads/{r.JobId}/status", r),
                onFailure: err => Results.BadRequest(new ProblemDetails
                {
                    Title = err.Code,
                    Detail = err.Message,
                    Status = 400
                }));
        })
        .WithName("RequestDownload")
        .RequireRateLimiting("ip-limit")
        .WithSummary("Queue a video download")
        .Produces<RequestDownloadResponse>(202)
        .ProducesProblem(400)
        .WithTags("Downloads");

        app.MapGet("/api/downloads/{jobId:guid}/status", async (
            Guid jobId,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new GetDownloadStatusQuery(jobId), ct);
            return result.Match(
                onSuccess: data => Results.Ok(data),
                onFailure: err => Results.NotFound(new ProblemDetails
                {
                    Title = err.Code,
                    Detail = err.Message,
                    Status = 404
                }));
        })
        .WithName("GetDownloadStatus")
        .WithSummary("Get download job status")
        .Produces<DownloadJobStatusResponse>()
        .ProducesProblem(404)
        .WithTags("Downloads");

        app.MapDelete("/api/downloads/{jobId:guid}", async (
            Guid jobId,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new CancelDownloadCommand(jobId), ct);
            return result.Match(
                onSuccess: _ => Results.NoContent(),
                onFailure: err => Results.Problem(
                    title: err.Code,
                    detail: err.Message,
                    statusCode: err == DomainErrors.DownloadJob.NotFound ? 404 : 409));
        })
        .WithName("CancelDownload")
        .WithSummary("Cancel a queued or running download")
        .Produces(204)
        .ProducesProblem(404)
        .ProducesProblem(409)
        .WithTags("Downloads");

        app.MapGet("/files/{fileName}", (string fileName, [FromQuery] string? title, IOptions<StorageOptions> storageOptions) =>
        {
            var basePath = Path.GetFullPath(storageOptions.Value.BasePath);
            var filePath = Path.GetFullPath(Path.Combine(basePath, fileName));
            // Only files directly in storage — never the .work dir or anything outside BasePath
            if (!string.Equals(Path.GetDirectoryName(filePath), basePath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal)
                || !File.Exists(filePath))
                return Results.NotFound();
            var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
            var contentType = ext == "mp3" ? "audio/mpeg" : "video/mp4";
            var downloadName = !string.IsNullOrWhiteSpace(title)
                ? $"{SanitizeFileName(title)}.{ext}"
                : fileName;
            // Range support: resumable downloads and seeking in the browser player
            return Results.File(filePath, contentType, downloadName, enableRangeProcessing: true);
        })
        .WithName("DownloadFile")
        .WithSummary("Download processed file")
        .WithTags("Downloads");
    }

    private static string SanitizeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(title.Select(c => invalid.Contains(c) ? '_' : c));
        return sanitized.Trim().TrimEnd('.');
    }
}
