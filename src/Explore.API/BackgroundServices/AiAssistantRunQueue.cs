using System.Threading.Channels;
using Explore.API.Hosting;

namespace Explore.API.BackgroundServices;

public sealed record AiAssistantRunQueueItem(
    Guid TenantId,
    Guid ConversationId,
    Guid RunId,
    string Mode)
{
    internal long AgentDatabaseGeneration { get; init; }
}

public interface IAiAssistantRunQueue
{
    ValueTask EnqueueAsync(AiAssistantRunQueueItem item, CancellationToken cancellationToken);

    IAsyncEnumerable<AiAssistantRunQueueItem> ReadAllAsync(CancellationToken cancellationToken);
}

public sealed class AiAssistantRunQueue(AgentBrowserResetCoordinator? agentDatabase = null) : IAiAssistantRunQueue
{
    private readonly Channel<AiAssistantRunQueueItem> _channel = Channel.CreateUnbounded<AiAssistantRunQueueItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public ValueTask EnqueueAsync(AiAssistantRunQueueItem item, CancellationToken cancellationToken)
        => _channel.Writer.WriteAsync(agentDatabase is null ? item
            : item with { AgentDatabaseGeneration = agentDatabase.Generation }, cancellationToken);

    public IAsyncEnumerable<AiAssistantRunQueueItem> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
