using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VideoDownloader.Domain.Entities;
using VideoDownloader.Domain.ValueObjects;

namespace VideoDownloader.Infrastructure.Persistence;

public sealed class DownloadJobConfiguration : IEntityTypeConfiguration<DownloadJob>
{
    public void Configure(EntityTypeBuilder<DownloadJob> builder)
    {
        builder.ToTable("download_jobs");
        builder.HasKey(j => j.Id);

        builder.Property(j => j.Id)
            .HasColumnName("id");

        var urlConverter = new ValueConverter<VideoUrl, string>(
            v => v.Value,
            s => VideoUrl.Create(s).Value!);

        builder.Property(j => j.Url)
            .HasConversion(urlConverter)
            .HasColumnName("url")
            .HasMaxLength(2048)
            .IsRequired();

        builder.Property(j => j.Format)
            .HasConversion<string>()
            .HasColumnName("format")
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(j => j.Quality)
            .HasColumnName("quality")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(j => j.Status)
            .HasConversion<string>()
            .HasColumnName("status")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(j => j.ProgressPercent)
            .HasColumnName("progress_percent");

        builder.Property(j => j.OutputFilePath)
            .HasColumnName("output_file_path")
            .HasMaxLength(1024);

        builder.Property(j => j.ErrorMessage)
            .HasColumnName("error_message")
            .HasMaxLength(2048);

        builder.Property(j => j.Title)
            .HasColumnName("title")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(j => j.ThumbnailUrl)
            .HasColumnName("thumbnail_url")
            .HasMaxLength(2048)
            .IsRequired();

        builder.Property(j => j.Duration)
            .HasColumnName("duration")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(j => j.FileSizeBytes)
            .HasColumnName("file_size_bytes");

        builder.Property(j => j.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(j => j.CompletedAt)
            .HasColumnName("completed_at");
    }
}
