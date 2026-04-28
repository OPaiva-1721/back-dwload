using Microsoft.Extensions.Logging;

namespace VideoDownloader.Infrastructure.Queue;

public sealed class FileCleanupJob(ILogger<FileCleanupJob> logger)
{
    public Task DeleteAsync(string filePath, CancellationToken ct)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            logger.LogInformation("Deleted temporary file {FilePath}", filePath);
        }
        return Task.CompletedTask;
    }
}
