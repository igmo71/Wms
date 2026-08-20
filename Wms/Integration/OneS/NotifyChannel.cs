using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace Wms.Integration.OneS;

internal class NotifyChannel(ILogger<NotifyChannel> logger)
{
    private readonly Channel<NotifyRecord> _channel = Channel.CreateUnbounded<NotifyRecord>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });

    public ChannelWriter<NotifyRecord> Writer => _channel.Writer;
    public ChannelReader<NotifyRecord> Reader => _channel.Reader;

    public ValueTask WriteAsync(NotifyRecord notifyRecord, CancellationToken ct = default)
    {
        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} - Enqueue {@NotifyRecord}", nameof(WriteAsync), notifyRecord);

        return _channel.Writer.WriteAsync(notifyRecord, ct);
    }
}
