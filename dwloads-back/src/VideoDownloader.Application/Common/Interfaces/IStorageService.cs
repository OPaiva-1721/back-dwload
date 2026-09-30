namespace VideoDownloader.Application.Common.Interfaces;

public interface IStorageService
{
    /// <summary>Moves a finished file into storage and returns its stored path.</summary>
    Task<string> ImportAsync(string sourcePath, string fileName, CancellationToken ct = default);
    Task DeleteAsync(string filePath, CancellationToken ct = default);
    string GetDownloadUrl(string filePath, string title = "");
}
