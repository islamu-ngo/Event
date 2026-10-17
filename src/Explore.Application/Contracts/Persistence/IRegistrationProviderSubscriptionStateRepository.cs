using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IRegistrationProviderSubscriptionStateRepository
{
    Task<RegistrationProviderSubscriptionState?> GetAsync(
        Guid tenantId,
        Guid registrationProviderBindingId,
        string providerEventType,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RegistrationProviderSubscriptionState>> ClaimDueRenewalsAsync(
        int batchSize,
        DateTime renewBefore,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RegistrationProviderSubscriptionState>> ClaimDueSweepsAsync(
        int batchSize,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RegistrationProviderSubscriptionState>> GetExpiringAsync(
        DateTime expiresBefore,
        int limit,
        CancellationToken cancellationToken);

    Task AddAsync(RegistrationProviderSubscriptionState state, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Worker-only custody; callback notification writers do not acquire this capability.</summary>
public interface IRegistrationProviderSubscriptionCustodyRepository : IRegistrationProviderSubscriptionStateRepository
{
    Task<RegistrationProviderSubscriptionState?> GetActiveClaimAsync(
        RegistrationProviderSubscriptionClaim claim, CancellationToken cancellationToken);

    /// <summary>Rechecks the original tuple and binding lineage against persisted state and a fresh clock.</summary>
    Task<bool> IsCurrentAsync(
        RegistrationProviderSubscriptionClaim claim,
        RegistrationProviderBinding? binding,
        CancellationToken cancellationToken);

    /// <summary>Requires the owning local transaction; takes the shared claim lock before the row fence.</summary>
    Task<RegistrationProviderSubscriptionState> FenceAsync(
        RegistrationProviderSubscriptionClaim claim,
        RegistrationProviderBinding? binding,
        CancellationToken cancellationToken);
}
