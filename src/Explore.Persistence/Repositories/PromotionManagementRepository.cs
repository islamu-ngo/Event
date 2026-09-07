using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services.Registration;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class PromotionManagementRepository(ExploreDbContext dbContext) : IPromotionManagementRepository
{
    public async Task<PromotionDefinition?> GetDefinitionForUpdateAsync(Guid tenantId, Guid eventId, Guid definitionId, CancellationToken cancellationToken)
    {
        await AcquirePromotionLockIfTransactionalAsync(tenantId, definitionId, cancellationToken);
        return await dbContext.PromotionDefinitions.FirstOrDefaultAsync(
            definition => definition.TenantId == tenantId && definition.Id == definitionId && EF.Property<Guid>(definition, "ScopeEventId") == eventId,
            cancellationToken);
    }

    public async Task<PromotionManagementEntry?> GetManagementEntryAsync(Guid tenantId, Guid eventId, Guid definitionId, CancellationToken cancellationToken)
    {
        PromotionDefinition? definition = await dbContext.PromotionDefinitions.AsNoTracking().FirstOrDefaultAsync(
            candidate => candidate.TenantId == tenantId && candidate.Id == definitionId && EF.Property<Guid>(candidate, "ScopeEventId") == eventId,
            cancellationToken);
        return definition is null ? null : new PromotionManagementEntry(definition, await GetActiveCodeAsync(tenantId, definition.Id, cancellationToken));
    }

    public async Task<IReadOnlyList<PromotionManagementEntry>> ListManagementEntriesAsync(Guid tenantId, Guid eventId, Guid ticketCatalogVersionId, CancellationToken cancellationToken)
    {
        PromotionDefinition[] definitions = await dbContext.PromotionDefinitions.AsNoTracking()
            .Where(definition => definition.TenantId == tenantId && EF.Property<Guid>(definition, "ScopeEventId") == eventId && EF.Property<Guid>(definition, "ScopeTicketCatalogVersionId") == ticketCatalogVersionId)
            .OrderBy(definition => definition.DefinitionGroupId)
            .ThenByDescending(definition => definition.VersionNumber)
            .ToArrayAsync(cancellationToken);
        Guid[] definitionIds = definitions.Select(definition => definition.Id).ToArray();
        Dictionary<Guid, PromotionCode> activeCodes = await dbContext.PromotionCodes.AsNoTracking()
            .Where(code => code.TenantId == tenantId && definitionIds.Contains(code.PromotionDefinitionVersionId) && EF.Property<bool>(code, "IsActive"))
            .ToDictionaryAsync(code => code.PromotionDefinitionVersionId, cancellationToken);
        return definitions.Select(definition => new PromotionManagementEntry(definition, activeCodes.GetValueOrDefault(definition.Id))).ToArray();
    }

    public async Task AddDefinitionAsync(PromotionDefinition definition, CancellationToken cancellationToken)
    {
        await dbContext.PromotionDefinitions.AddAsync(definition, cancellationToken);
        SetScope(definition, definition.ScopeMetadata);
    }

    public async Task AddPublishedCodeAsync(PromotionCode code, PromotionCodeDigest digest, CancellationToken cancellationToken)
    {
        await dbContext.PromotionCodes.AddAsync(code, cancellationToken);
        SetScope(code, code.ScopeMetadata);
        SetDigest(code, digest, isActive: true, retiredAtUtc: null);
    }

    public async Task ReplaceActiveCodeAsync(PromotionDefinition definition, PromotionCode code, PromotionCodeDigest digest, DateTime rotatedAtUtc, CancellationToken cancellationToken)
    {
        await AcquirePromotionLockIfTransactionalAsync(definition.TenantId, definition.Id, cancellationToken);
        Guid[] activeCodeIds = await dbContext.PromotionCodes.AsNoTracking()
            .Where(existing => existing.TenantId == definition.TenantId && existing.PromotionDefinitionVersionId == definition.Id && EF.Property<bool>(existing, "IsActive"))
            .Select(existing => existing.Id)
            .OrderBy(id => id)
            .ToArrayAsync(cancellationToken);
        foreach (Guid activeCodeId in activeCodeIds)
        {
            await AcquirePromotionCodeLockIfTransactionalAsync(definition.TenantId, activeCodeId, cancellationToken);
        }

        await dbContext.PromotionCodes
            .Where(existing => existing.TenantId == definition.TenantId && existing.PromotionDefinitionVersionId == definition.Id && EF.Property<bool>(existing, "IsActive"))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(existing => EF.Property<bool>(existing, "IsActive"), false)
                .SetProperty(existing => EF.Property<DateTime?>(existing, "RetiredAtUtc"), rotatedAtUtc), cancellationToken);
        await AddPublishedCodeAsync(code, digest, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    private Task<PromotionCode?> GetActiveCodeAsync(Guid tenantId, Guid definitionId, CancellationToken cancellationToken) =>
        dbContext.PromotionCodes.AsNoTracking().FirstOrDefaultAsync(
            code => code.TenantId == tenantId && code.PromotionDefinitionVersionId == definitionId && EF.Property<bool>(code, "IsActive"),
            cancellationToken);

    private async Task AcquirePromotionLockIfTransactionalAsync(Guid tenantId, Guid definitionId, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            await RelationalNamedLock.AcquireTransactionAsync(dbContext, $"promotion:{tenantId:N}:{definitionId:N}", cancellationToken);
        }
    }

    private async Task AcquirePromotionCodeLockIfTransactionalAsync(Guid tenantId, Guid codeId, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            await RelationalNamedLock.AcquireTransactionAsync(dbContext, $"promotion-code:{tenantId:N}:{codeId:N}", cancellationToken);
        }
    }

    private void SetDigest(PromotionCode code, PromotionCodeDigest digest, bool isActive, DateTime? retiredAtUtc)
    {
        dbContext.Entry(code).Property("LookupKeyVersion").CurrentValue = digest.KeyVersion;
        dbContext.Entry(code).Property("LookupDigest").CurrentValue = digest.Value;
        dbContext.Entry(code).Property("IsActive").CurrentValue = isActive;
        dbContext.Entry(code).Property("RetiredAtUtc").CurrentValue = retiredAtUtc;
    }

    private void SetScope(object entity, PromotionScopeMetadata scope)
    {
        dbContext.Entry(entity).Property("ScopeEventId").CurrentValue = scope.EventId;
        dbContext.Entry(entity).Property("ScopeTicketCatalogVersionId").CurrentValue = scope.TicketCatalogVersionId;
        dbContext.Entry(entity).Property("ScopeTicketCatalogVersionNumber").CurrentValue = scope.TicketCatalogVersionNumber;
        dbContext.Entry(entity).Property("ScopeCurrencyCode").CurrentValue = scope.CurrencyCode;
    }
}
