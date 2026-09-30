using MediatR;
using VideoDownloader.Application.Common.DTOs;
using VideoDownloader.Application.Common.Interfaces;
using VideoDownloader.Domain.Entities;
using VideoDownloader.Domain.Enums;
using VideoDownloader.Domain.Errors;
using VideoDownloader.Domain.Interfaces;
using VideoDownloader.Domain.ValueObjects;

namespace VideoDownloader.Application.Downloads.Commands.RequestDownload;

public sealed class RequestDownloadHandler(
    IDownloadJobRepository repository,
    IDownloadQueue queue)
    : IRequestHandler<RequestDownloadCommand, Result<RequestDownloadResponse>>
{
    public async Task<Result<RequestDownloadResponse>> Handle(
        RequestDownloadCommand request, CancellationToken ct)
    {
        var urlResult = VideoUrl.Create(request.Url);
        if (urlResult.IsFailure)
            return Result<RequestDownloadResponse>.Failure(urlResult.Error);

        if (!Enum.TryParse<DownloadFormat>(request.Format, ignoreCase: true, out var format))
            return Result<RequestDownloadResponse>.Failure(
                new Error("Format.Invalid", $"Format '{request.Format}' is not supported. Use 'mp4' or 'mp3'."));

        if (!IsValidQuality(format, request.Quality))
            return Result<RequestDownloadResponse>.Failure(
                new Error("Quality.Invalid", "That quality isn't available. Pick one of the listed options."));

        var job = DownloadJob.Create(urlResult.Value!, format, request.Quality, request.Title, request.ThumbnailUrl, request.Duration);

        await repository.AddAsync(job, ct);
        await queue.EnqueueAsync(job.Id, ct);

        return Result<RequestDownloadResponse>.Success(
            new RequestDownloadResponse(job.Id, job.Status.ToString()));
    }

    private static readonly string[] AudioQualities = ["320kbps", "256kbps", "192kbps", "128kbps"];

    // Video: any real stream height ("360p", "1440p"...), since clients offer what the video actually has
    private static bool IsValidQuality(DownloadFormat format, string quality) =>
        format == DownloadFormat.Mp3
            ? AudioQualities.Contains(quality)
            : quality.EndsWith('p')
              && int.TryParse(quality.AsSpan(0, quality.Length - 1), out var height)
              && height is >= 144 and <= 4320;
}
