using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Single-assignment persisted custody, checked before consumer parsing or effects.</summary>
public interface IGenericOutboxExecutionAuthority
{
    Task BindAsync(GenericOutboxClaim claim, CancellationToken cancellationToken);
    Task RequireAsync(OutboxMessage message, CancellationToken cancellationToken);
    Task RequireDeadLetterAsync(OutboxMessage message, CancellationToken cancellationToken);
}
