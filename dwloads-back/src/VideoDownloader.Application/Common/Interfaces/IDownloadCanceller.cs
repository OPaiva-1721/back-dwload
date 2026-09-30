namespace VideoDownloader.Application.Common.Interfaces;

/// <summary>Stops a download that is currently running on this instance.</summary>
public interface IDownloadCanceller
{
    /// <returns>True if the job was running here and has been signalled to stop.</returns>
    bool TryCancel(Guid jobId);
}
