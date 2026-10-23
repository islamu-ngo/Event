using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Requires current persisted notification custody in an already selected exact
/// Runtime tenant. Call before normalizing a retained payload or invoking fanout.
/// Does not bind or retarget a tenant context.
/// </summary>
public interface INotificationOutboxExecutionAuthority
{
    Task BindAsync(NotificationOutboxClaim claim, CancellationToken cancellationToken);

    Task RequireAsync(OutboxMessage message, CancellationToken cancellationToken);
}
