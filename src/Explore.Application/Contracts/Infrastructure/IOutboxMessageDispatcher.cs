using Explore.Domain;

namespace Explore.Application.Contracts.Infrastructure;

/// <summary>
/// Dispatches an outbox message to its consumer. The processor calls this after claiming
/// a message via optimistic lock. Implementations should route based on
/// <see cref="OutboxMessage.EventType"/> and must be idempotent — the same message may
/// be dispatched more than once on retry after a crash.
/// </summary>
public interface IOutboxMessageDispatcher
{
    /// <summary>
    /// Dispatches the message to its consumer. Throw on failure to trigger retry logic.
    /// </summary>
    Task DispatchAsync(OutboxMessage message, CancellationToken ct = default);

    Task ReconcileDeadLetterAsync(OutboxMessage message, CancellationToken ct = default);
}
