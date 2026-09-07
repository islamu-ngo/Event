using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IOrganizerPaymentProviderAccountOperationRepository
{
    Task<OrganizerPaymentProviderAccountOperation?> GetActiveByScopeAsync(
        Guid tenantId,
        Guid organizerActorId,
        string providerCode,
        string connectPlatformId,
        CancellationToken cancellationToken);

    Task<OrganizerPaymentProviderAccountOperation?> GetByTenantAndIdForUpdateAsync(
        Guid tenantId,
        Guid operationId,
        CancellationToken cancellationToken);

    Task CreateAsync(OrganizerPaymentProviderAccountOperation operation, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
