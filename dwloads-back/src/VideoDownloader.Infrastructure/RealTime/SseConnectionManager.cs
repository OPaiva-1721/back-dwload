using System.Collections.Concurrent;
using System.Threading.Channels;

namespace VideoDownloader.Infrastructure.RealTime;

public sealed class SseConnectionManager
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> _channels = new();

    public Channel<string> GetOrCreate(Guid jobId) =>
        _channels.GetOrAdd(jobId, _ => Channel.CreateBounded<string>(
            new BoundedChannelOptions(50) { FullMode = BoundedChannelFullMode.DropOldest }));

    public void TryComplete(Guid jobId)
    {
        if (_channels.TryRemove(jobId, out var channel))
            channel.Writer.TryComplete();
    }
}
