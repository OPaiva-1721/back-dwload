using Microsoft.Extensions.Options;
using VideoDownloader.Application.Common.Interfaces;

namespace VideoDownloader.Infrastructure.Storage;

public sealed class LocalStorageService(IOptions<StorageOptions> options) : IStorageService
{
    private readonly string _basePath = options.Value.BasePath;
    private readonly string _baseUrl = options.Value.BaseUrl;

    public async Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct)
    {
        Directory.CreateDirectory(_basePath);
        var dest = Path.Combine(_basePath, fileName);
        await using var fs = File.Create(dest);
        await content.CopyToAsync(fs, ct);
        return dest;
    }

    public Task DeleteAsync(string filePath, CancellationToken ct)
    {
        if (File.Exists(filePath)) File.Delete(filePath);
        return Task.CompletedTask;
    }

    public string GetDownloadUrl(string filePath, string title = "")
    {
        var url = $"{_baseUrl.TrimEnd('/')}/files/{Path.GetFileName(filePath)}";
        return string.IsNullOrWhiteSpace(title)
            ? url
            : $"{url}?title={Uri.EscapeDataString(title)}";
    }
}
