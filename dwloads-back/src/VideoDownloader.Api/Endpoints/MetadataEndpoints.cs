using MediatR;
using Microsoft.AspNetCore.Mvc;
using VideoDownloader.Application.Common.DTOs;
using VideoDownloader.Application.Downloads.Queries.GetVideoMetadata;

namespace VideoDownloader.Api.Endpoints;

public static class MetadataEndpoints
{
    public static void MapMetadataEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/metadata", async (
            [FromQuery] string url,
            ISender sender,
            CancellationToken ct) =>
        {
            var result = await sender.Send(new GetVideoMetadataQuery(url), ct);
            return result.Match(
                onSuccess: data => Results.Ok(data),
                onFailure: err => Results.BadRequest(new ProblemDetails
                {
                    Title = err.Code,
                    Detail = err.Message,
                    Status = 400
                }));
        })
        .WithName("GetMetadata")
        .WithSummary("Fetch video metadata from URL")
        .Produces<VideoMetadataResponse>()
        .ProducesProblem(400)
        .WithTags("Metadata");
    }
}
