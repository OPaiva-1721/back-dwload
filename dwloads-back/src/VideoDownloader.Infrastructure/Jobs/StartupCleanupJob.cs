using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VideoDownloader.Infrastructure.Storage;

namespace VideoDownloader.Infrastructure.Jobs;

public sealed class StartupCleanupJob(
    IOptions<StorageOptions> storageOptions,
    ILogger<StartupCleanupJob> logger) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        var opts = storageOptions.Value;
        if (!Directory.Exists(opts.BasePath))
        {
            logger.LogInformation("Storage directory {Path} does not exist, skipping startup cleanup", opts.BasePath);
            return Task.CompletedTask;
        }

        var cutoff = DateTimeOffset.UtcNow - opts.FileRetention;
        var deleted = 0;

        // Work dir holds partial downloads and cached info JSONs left behind by a crash/restart
        var files = Directory.GetFiles(opts.BasePath)
            .Concat(Directory.Exists(opts.WorkPath) ? Directory.GetFiles(opts.WorkPath) : []);

        foreach (var file in files)
        {
            try
            {
                var lastWrite = new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero);
                if (lastWrite < cutoff)
                {
                    File.Delete(file);
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete expired file {File} during startup cleanup", file);
            }
        }

        if (deleted > 0)
            logger.LogInformation("Startup cleanup: deleted {Count} expired file(s) from {Path}", deleted, opts.BasePath);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
