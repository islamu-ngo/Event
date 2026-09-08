using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.References;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class CustomPropertyDefinitionRepository : GenericRepository<CustomPropertyDefinition, Guid>, ICustomPropertyDefinitionRepository
{
    private readonly ExploreDbContext _dbContext;

    public CustomPropertyDefinitionRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public override async Task<CustomPropertyDefinition> Create(CustomPropertyDefinition entity)
    {
        EnsureSharedDefinitionTarget(entity);
        return await base.Create(entity);
    }

    public override async Task Update(CustomPropertyDefinition entity)
    {
        EnsureSharedDefinitionTarget(entity);
        await base.Update(entity);
    }

    public async Task<CustomPropertyDefinition?> GetDefinitionWithDetails(Guid id)
    {
        return await _dbContext.CustomPropertyDefinitions
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Include(x => x.DefaultOption)
            .Include(x => x.Options.OrderBy(option => option.SortOrder))
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<CustomPropertyDefinition?> GetTrackedDefinitionWithOptions(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.CustomPropertyDefinitions
            .Include(x => x.Options.OrderBy(option => option.SortOrder))
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<(List<CustomPropertyDefinition> Items, int TotalCount)> GetDefinitionsWithDetailsPaged(
        EntityTypeName entityTypeName,
        int pageNumber,
        int pageSize)
    {
        var query = _dbContext.CustomPropertyDefinitions
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Where(x => x.EntityTypeName == entityTypeName)
            .Include(x => x.Options)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.DisplayName);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<int> CountDefinitionsForScope(Guid tenantId, EntityTypeName entityTypeName, CancellationToken cancellationToken)
    {
        return await _dbContext.CustomPropertyDefinitions
            .CountAsync(x => x.TenantId == tenantId && x.EntityTypeName == entityTypeName, cancellationToken);
    }

    public async Task<bool> ExistsScopedMachineKey(Guid tenantId, EntityTypeName entityTypeName, string namespaceValue, string key, Guid? excludeDefinitionId = null)
    {
        return await _dbContext.CustomPropertyDefinitions
            .AnyAsync(x => x.TenantId == tenantId
                && x.EntityTypeName == entityTypeName
                && x.Namespace == namespaceValue
                && x.Key == key
                && (!excludeDefinitionId.HasValue || x.Id != excludeDefinitionId.Value));
    }

    public async Task<CustomPropertyDefinition> CreateWithOptions(
        CustomPropertyDefinition definition,
        IReadOnlyCollection<CustomPropertyOption> options,
        Guid? defaultOptionId,
        CancellationToken cancellationToken)
    {
        EnsureSharedDefinitionTarget(definition);

        await _dbContext.CustomPropertyDefinitions.AddAsync(definition, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (options.Count > 0)
        {
            await _dbContext.CustomPropertyOptions.AddRangeAsync(options, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        if (defaultOptionId.HasValue)
        {
            definition.DefaultOptionId = defaultOptionId;
            _dbContext.Entry(definition).State = EntityState.Modified;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return await GetDefinitionWithDetails(definition.Id) ?? definition;
    }

    public async Task<CustomPropertyDefinition> UpdateWithOptions(
        CustomPropertyDefinition definition,
        IReadOnlyCollection<CustomPropertyOption> options,
        Guid? defaultOptionId,
        CancellationToken cancellationToken)
    {
        EnsureSharedDefinitionTarget(definition);

        var existingOptions = await _dbContext.CustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.CustomPropertyDefinitionId == definition.Id)
            .ToListAsync(cancellationToken);

        var incomingKeys = options
            .Select(x => (x.Namespace, x.Key))
            .ToHashSet();
        var existingByKey = existingOptions.ToDictionary(x => (x.Namespace, x.Key));
        Guid? persistedDefaultOptionId = null;

        foreach (var option in options)
        {
            if (existingByKey.TryGetValue((option.Namespace, option.Key), out var existing))
            {
                existing.DisplayName = option.DisplayName;
                existing.Description = option.Description;
                existing.Value = option.Value;
                existing.IsDefault = option.IsDefault;
                existing.IsActive = option.IsActive;
                existing.SortOrder = option.SortOrder;
                existing.ParentOptionId = option.ParentOptionId;
                existing.UpdatedAt = option.UpdatedAt;
                existing.UpdatedBy = option.UpdatedBy;
                existing.IsDeleted = false;
                existing.DeletedAt = null;
                existing.DeletedBy = null;

                if (existing.IsDefault)
                {
                    persistedDefaultOptionId = existing.Id;
                }

                continue;
            }

            await _dbContext.CustomPropertyOptions.AddAsync(option, cancellationToken);
            if (option.IsDefault)
            {
                persistedDefaultOptionId = option.Id;
            }
        }

        foreach (var existing in existingOptions.Where(x => !incomingKeys.Contains((x.Namespace, x.Key))))
        {
            existing.IsDefault = false;
            existing.IsActive = false;
        }

        definition.DefaultOptionId = persistedDefaultOptionId;
        _dbContext.Entry(definition).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetDefinitionWithDetails(definition.Id) ?? definition;
    }

    public async Task<bool> DeleteDefinition(Guid id, CancellationToken cancellationToken)
    {
        var definition = await _dbContext.CustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (definition == null)
        {
            return false;
        }

        var options = await _dbContext.CustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.CustomPropertyDefinitionId == id)
            .ToListAsync(cancellationToken);
        var values = await _dbContext.CustomPropertyValues
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.CustomPropertyDefinitionId == id)
            .ToListAsync(cancellationToken);

        definition.IsActive = false;
        definition.DefaultOptionId = null;
        definition.IsDeleted = true;
        definition.DeletedAt ??= DateTime.UtcNow;

        foreach (var option in options)
        {
            option.IsDefault = false;
            option.IsActive = false;
            option.IsDeleted = true;
            option.DeletedAt ??= definition.DeletedAt;
        }

        foreach (var value in values.Where(x => !x.IsDeleted))
        {
            value.IsDeleted = true;
            value.DeletedAt ??= definition.DeletedAt;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<CustomPropertyPurgeDependencySummary?> GetPurgeDependencies(Guid id, CancellationToken cancellationToken)
    {
        var definition = await _dbContext.CustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .AsNoTracking()
            .Select(x => new { x.Id, x.TenantId })
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (definition is null)
        {
            return null;
        }

        var optionCount = await _dbContext.CustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .CountAsync(x => x.CustomPropertyDefinitionId == id, cancellationToken);
        var valueCount = await _dbContext.CustomPropertyValues
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .CountAsync(x => x.CustomPropertyDefinitionId == id, cancellationToken);
        var auditCount = await _dbContext.AuditLogs
            .CountAsync(x => x.EntityType == nameof(CustomPropertyDefinition) && x.EntityId == id.ToString(), cancellationToken);

        return new CustomPropertyPurgeDependencySummary(
            id,
            definition.TenantId,
            "custom_property_definition",
            optionCount,
            valueCount,
            ProjectionCount: 0,
            auditCount,
            SyncProvenanceCount: 0);
    }

    public async Task<bool> PurgeDefinition(Guid id, CancellationToken cancellationToken)
    {
        var dependencies = await GetPurgeDependencies(id, cancellationToken);
        if (dependencies is null || dependencies.HasBlockingDependencies)
        {
            return false;
        }

        await _dbContext.CustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.DefaultOptionId, (Guid?)null), cancellationToken);

        await _dbContext.CustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.CustomPropertyDefinitionId == id)
            .ExecuteDeleteAsync(cancellationToken);

        var deleted = await _dbContext.CustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted > 0;
    }

    private static void EnsureSharedDefinitionTarget(CustomPropertyDefinition definition)
    {
        var errors = ReferenceTypeRegistry.ValidateSharedCustomPropertyDefinition(definition);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(' ', errors));
        }
    }
}
