using Hangfire;
using Hangfire.InMemory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VideoDownloader.Application.Common.Interfaces;
using VideoDownloader.Domain.Interfaces;
using VideoDownloader.Infrastructure.Queue;
using VideoDownloader.Infrastructure.RealTime;
using VideoDownloader.Infrastructure.Repositories;
using VideoDownloader.Infrastructure.Storage;
using VideoDownloader.Infrastructure.YtDlp;

namespace VideoDownloader.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        services.Configure<YtDlpOptions>(config.GetSection("YtDlp"));
        services.Configure<StorageOptions>(config.GetSection("Storage"));

        services.AddSingleton<IDownloadJobRepository, InMemoryDownloadJobRepository>();
        services.AddScoped<IVideoMetadataService, YtDlpVideoService>();
        services.AddScoped<IVideoDownloadService, YtDlpVideoService>();
        services.AddScoped<IStorageService, LocalStorageService>();
        services.AddScoped<IDownloadQueue, HangfireDownloadQueue>();
        services.AddSingleton<SseConnectionManager>();
        services.AddScoped<IProgressNotifier, SseProgressNotifier>();

        services.AddScoped<DownloadJobProcessor>();
        services.AddScoped<FileCleanupJob>();

        services.AddHangfire(cfg => cfg.UseInMemoryStorage());
        services.AddHangfireServer(opts => opts.WorkerCount = 2);

        return services;
    }
}
