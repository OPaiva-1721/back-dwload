using FluentValidation;

namespace VideoDownloader.Api.Contracts;

public sealed record DownloadRequest(string Url, string Format, string Quality, string? Title = null, string? ThumbnailUrl = null, string? Duration = null);

public sealed class DownloadRequestValidator : AbstractValidator<DownloadRequest>
{
    private static readonly string[] ValidVideoQualities = ["2160p", "1080p", "720p", "480p"];
    private static readonly string[] ValidAudioQualities = ["320kbps", "256kbps", "192kbps", "128kbps"];

    public DownloadRequestValidator()
    {
        RuleFor(x => x.Url).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.Format)
            .NotEmpty()
            .Must(f => f.Equals("mp4", StringComparison.OrdinalIgnoreCase)
                    || f.Equals("mp3", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Format must be 'mp4' or 'mp3'.");
        RuleFor(x => x.Quality)
            .NotEmpty()
            .Must((req, q) =>
                req.Format.Equals("mp4", StringComparison.OrdinalIgnoreCase)
                    ? ValidVideoQualities.Contains(q)
                    : ValidAudioQualities.Contains(q))
            .WithMessage("Invalid quality for the selected format.");
    }
}
