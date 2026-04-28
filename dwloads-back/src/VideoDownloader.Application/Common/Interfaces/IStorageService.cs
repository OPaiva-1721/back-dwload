namespace VideoDownloader.Application.Common.Interfaces;

public interface IStorageService
{
    Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct = default);
    Task DeleteAsync(string filePath, CancellationToken ct = default);
    string GetDownloadUrl(string filePath, string title = "");
}
