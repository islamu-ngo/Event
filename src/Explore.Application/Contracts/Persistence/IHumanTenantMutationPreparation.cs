using Explore.Application.Features.Tenants.Requests.Commands;

namespace Explore.Application.Contracts.Persistence;

public enum HumanTenantMutationKind
{
    Delete,
    RegistrationProviderConnections
}

public interface IHumanTenantMutationPreparation
{
    /// <summary>
    /// Prepares the original Delete command outside transactions, then reserves discovery before
    /// acquiring human sources and deleting in one physical Serializable owner.
    /// </summary>
    Task<bool> ExecuteDeletionAsync(DeleteTenantCommand request, ITenantRepository repository,
        CancellationToken cancellationToken = default);

    Task<IPreparedHumanTenantMutation> PrepareAsync(HumanTenantMutationKind mutation, Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<T> ExecuteAsync<T>(HumanTenantMutationKind mutation, Guid tenantId,
        Func<IPreparedHumanTenantMutation, CancellationToken, Task<T>> originalOwnerAttempt,
        CancellationToken cancellationToken = default);

    Task RequireOwnedAsync(IPreparedHumanTenantMutation prepared, Guid tenantId,
        HumanTenantMutationKind mutation, CancellationToken cancellationToken = default);

    Task<T> ExecutePreparedAsync<T>(HumanTenantMutationKind mutation, Guid tenantId,
        IPreparedHumanTenantMutation prepared,
        Func<IPreparedHumanTenantMutation, CancellationToken, Task<T>> originalOwnerAttempt,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// In-process, single-attempt capability issued only after the selected provider accepts
/// observed inputs. The original Serializable business owner acquires it before effects.
/// A rollback/retry requires a new preparation, never reuse of this capability.
/// </summary>
public interface IPreparedHumanTenantMutation
{
}
