using Explore.Domain;

namespace Explore.Application.Contracts.Infrastructure;

/// <summary>Returns only when the native effect's required work is durably complete; failures retain outbox retry.</summary>
public interface IModerationEffectDispatcher
{
    Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
