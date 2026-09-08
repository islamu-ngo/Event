using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IOrganizerPaymentProviderConnectionRepository
{
    Task<OrganizerPaymentProviderConnection?> GetActiveByScopeAsync(
        Guid tenantId,
        Guid organizerActorId,
        string providerCode,
        string connectPlatformId,
        CancellationToken cancellationToken);

    Task<OrganizerPaymentProviderConnection?> GetHistoricalByExternalAccountAsync(
        string providerCode,
        string connectPlatformId,
        string externalAccountId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganizerPaymentProviderConnection>> ListHistoricalByExternalAccountAsync(
        string providerCode,
        string externalAccountId,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganizerPaymentProviderConnection>> ListDueReadinessChecksAsync(
        DateTime observedBefore,
        int limit,
        CancellationToken cancellationToken);

    Task<OrganizerPaymentProviderConnection?> GetByTenantProviderAndExternalAccountForUpdateAsync(
        Guid tenantId,
        string providerCode,
        string externalAccountId,
        CancellationToken cancellationToken);

    Task<OrganizerPaymentProviderConnection?> GetByTenantAndIdForUpdateAsync(
        Guid tenantId,
        Guid connectionId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganizerPaymentProviderConnection>> ListByTenantAndActorAsync(
        Guid tenantId,
        Guid organizerActorId,
        CancellationToken cancellationToken);

    Task CreateAsync(OrganizerPaymentProviderConnection connection, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
