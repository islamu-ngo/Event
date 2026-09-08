using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class OrganizerPaymentProviderConnectionRepository(ExploreDbContext dbContext)
    : IOrganizerPaymentProviderConnectionRepository
{
    public Task<OrganizerPaymentProviderConnection?> GetActiveByScopeAsync(
        Guid tenantId,
        Guid organizerActorId,
        string providerCode,
        string connectPlatformId,
        CancellationToken cancellationToken) =>
        dbContext.OrganizerPaymentProviderConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(connection =>
                connection.TenantId == tenantId
                && connection.OrganizerActorId == organizerActorId
                && connection.ProviderCode == providerCode
                && connection.ConnectPlatformId == connectPlatformId
                && connection.StatusId != (int)OrganizerPaymentProviderConnectionStatusEnum.Disabled
                && connection.StatusId != (int)OrganizerPaymentProviderConnectionStatusEnum.Replaced,
                cancellationToken);

    public Task<OrganizerPaymentProviderConnection?> GetHistoricalByExternalAccountAsync(
        string providerCode,
        string connectPlatformId,
        string externalAccountId,
        CancellationToken cancellationToken) =>
        dbContext.OrganizerPaymentProviderConnections
            .IgnoreAllFilters(TenantFilterBypassReasons.OrganizerPaymentExternalAccountOwnershipCheck)
            .AsNoTracking()
            .FirstOrDefaultAsync(connection =>
                connection.ProviderCode == providerCode
                && connection.ConnectPlatformId == connectPlatformId
                && connection.ExternalAccountId == externalAccountId,
                cancellationToken);

    public async Task<IReadOnlyList<OrganizerPaymentProviderConnection>> ListHistoricalByExternalAccountAsync(
        string providerCode,
        string externalAccountId,
        int limit,
        CancellationToken cancellationToken) =>
        await dbContext.OrganizerPaymentProviderConnections
            .IgnoreAllFilters(TenantFilterBypassReasons.OrganizerPaymentExternalAccountOwnershipCheck)
            .AsNoTracking()
            .Where(connection =>
                connection.ProviderCode == providerCode
                && connection.ExternalAccountId == externalAccountId)
            .OrderBy(connection => connection.CreatedAt)
            .ThenBy(connection => connection.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OrganizerPaymentProviderConnection>> ListDueReadinessChecksAsync(
        DateTime observedBefore,
        int limit,
        CancellationToken cancellationToken) =>
        await dbContext.OrganizerPaymentProviderConnections
            .AsNoTracking()
            .IgnoreTenantFilter(TenantFilterBypassReasons.OrganizerPaymentReadinessWorkerCrossTenantQueue)
            .Where(connection =>
                !connection.IsDeleted
                && (connection.StatusId == (int)OrganizerPaymentProviderConnectionStatusEnum.PendingOnboarding
                    || connection.StatusId == (int)OrganizerPaymentProviderConnectionStatusEnum.Restricted)
                && (connection.LastReadinessObservedAt == null || connection.LastReadinessObservedAt < observedBefore))
            .OrderBy(connection => connection.LastReadinessObservedAt == null ? 0 : 1)
            .ThenBy(connection => connection.LastReadinessObservedAt)
            .ThenBy(connection => connection.CreatedAt)
            .ThenBy(connection => connection.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task<OrganizerPaymentProviderConnection?> GetByTenantProviderAndExternalAccountForUpdateAsync(
        Guid tenantId,
        string providerCode,
        string externalAccountId,
        CancellationToken cancellationToken) =>
        dbContext.OrganizerPaymentProviderConnections
            .FirstOrDefaultAsync(connection =>
                connection.TenantId == tenantId
                && connection.ProviderCode == providerCode
                && connection.ExternalAccountId == externalAccountId,
                cancellationToken);

    public Task<OrganizerPaymentProviderConnection?> GetByTenantAndIdForUpdateAsync(
        Guid tenantId,
        Guid connectionId,
        CancellationToken cancellationToken) =>
        dbContext.OrganizerPaymentProviderConnections
            .FirstOrDefaultAsync(connection => connection.TenantId == tenantId && connection.Id == connectionId, cancellationToken);

    public async Task<IReadOnlyList<OrganizerPaymentProviderConnection>> ListByTenantAndActorAsync(
        Guid tenantId,
        Guid organizerActorId,
        CancellationToken cancellationToken) =>
        await dbContext.OrganizerPaymentProviderConnections
            .AsNoTracking()
            .Where(connection => connection.TenantId == tenantId && connection.OrganizerActorId == organizerActorId)
            .OrderBy(connection => connection.CreatedAt)
            .ThenBy(connection => connection.Id)
            .ToListAsync(cancellationToken);

    public async Task CreateAsync(OrganizerPaymentProviderConnection connection, CancellationToken cancellationToken) =>
        await dbContext.OrganizerPaymentProviderConnections.AddAsync(connection, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
