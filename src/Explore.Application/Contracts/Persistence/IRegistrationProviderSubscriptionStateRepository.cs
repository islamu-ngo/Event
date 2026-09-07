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
