using System.Threading.Channels;

namespace Explore.API.BackgroundServices;

public sealed record AiAssistantRunQueueItem(
    Guid TenantId,
    Guid ConversationId,
    Guid RunId,
    string Mode);

public interface IAiAssistantRunQueue
{
    ValueTask EnqueueAsync(AiAssistantRunQueueItem item, CancellationToken cancellationToken);

    IAsyncEnumerable<AiAssistantRunQueueItem> ReadAllAsync(CancellationToken cancellationToken);
}

public sealed class AiAssistantRunQueue : IAiAssistantRunQueue
{
    private readonly Channel<AiAssistantRunQueueItem> _channel = Channel.CreateUnbounded<AiAssistantRunQueueItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public ValueTask EnqueueAsync(AiAssistantRunQueueItem item, CancellationToken cancellationToken)
        => _channel.Writer.WriteAsync(item, cancellationToken);

    public IAsyncEnumerable<AiAssistantRunQueueItem> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
