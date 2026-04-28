using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VideoDownloader.Application.Common.Interfaces;
using VideoDownloader.Domain.Interfaces;
using VideoDownloader.Infrastructure.Persistence;
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

        var connectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

        services.AddDbContext<AppDbContext>(opts =>
            opts.UseNpgsql(connectionString));

        services.AddScoped<IDownloadJobRepository, EfDownloadJobRepository>();
        services.AddScoped<IVideoMetadataService, YtDlpVideoService>();
        services.AddScoped<IVideoDownloadService, YtDlpVideoService>();
        services.AddScoped<IStorageService, LocalStorageService>();
        services.AddScoped<IDownloadQueue, HangfireDownloadQueue>();
        services.AddSingleton<SseConnectionManager>();
        services.AddScoped<IProgressNotifier, SseProgressNotifier>();

        services.AddScoped<DownloadJobProcessor>();
        services.AddScoped<FileCleanupJob>();

        services.AddHangfire(cfg =>
            cfg.UsePostgreSqlStorage(o =>
                o.UseNpgsqlConnection(connectionString)));
        services.AddHangfireServer(opts => opts.WorkerCount = 2);

        return services;
    }
}
