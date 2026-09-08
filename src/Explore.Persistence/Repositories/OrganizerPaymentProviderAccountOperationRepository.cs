using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class OrganizerPaymentProviderAccountOperationRepository(ExploreDbContext dbContext)
    : IOrganizerPaymentProviderAccountOperationRepository
{
    public Task<OrganizerPaymentProviderAccountOperation?> GetActiveByScopeAsync(
        Guid tenantId,
        Guid organizerActorId,
        string providerCode,
        string connectPlatformId,
        CancellationToken cancellationToken) =>
        dbContext.OrganizerPaymentProviderAccountOperations
            .FirstOrDefaultAsync(operation =>
                operation.TenantId == tenantId
                && operation.OrganizerActorId == organizerActorId
                && operation.ProviderCode == providerCode
                && operation.ConnectPlatformId == connectPlatformId
                && operation.ActiveUniquenessSlot == "active",
                cancellationToken);

    public Task<OrganizerPaymentProviderAccountOperation?> GetByTenantAndIdForUpdateAsync(
        Guid tenantId,
        Guid operationId,
        CancellationToken cancellationToken) =>
        dbContext.OrganizerPaymentProviderAccountOperations
            .FirstOrDefaultAsync(operation => operation.TenantId == tenantId && operation.Id == operationId, cancellationToken);

    public async Task CreateAsync(OrganizerPaymentProviderAccountOperation operation, CancellationToken cancellationToken) =>
        await dbContext.OrganizerPaymentProviderAccountOperations.AddAsync(operation, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
