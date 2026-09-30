using System.Globalization;

namespace VideoDownloader.Infrastructure.Storage;

public static class FileSizeFormatter
{
    // Invariant: the UI is English, and the server's culture (e.g. pt-BR) would render "5,1 MB"
    public static string Format(long bytes) => bytes switch
    {
        >= 1_073_741_824 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1_073_741_824.0:F1} GB"),
        >= 1_048_576 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1_048_576.0:F1} MB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:F1} KB"),
    };
}
