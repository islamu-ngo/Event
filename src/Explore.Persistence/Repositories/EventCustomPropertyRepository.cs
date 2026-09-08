using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class EventCustomPropertyRepository : GenericRepository<EventCustomPropertyDefinition, Guid>, IEventCustomPropertyRepository
{
    private readonly ExploreDbContext _dbContext;

    public EventCustomPropertyRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EventCustomPropertyDefinition?> GetDefinitionWithDetails(Guid id)
    {
        return await _dbContext.EventCustomPropertyDefinitions
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Include(x => x.DefaultOption)
            .Include(x => x.Options.OrderBy(o => o.SortOrder))
            .Include(x => x.Values.OrderBy(v => v.Ordinal))
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<EventCustomPropertyDefinition?> GetTrackedDefinitionWithOptions(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.EventCustomPropertyDefinitions
            .Include(x => x.Options.OrderBy(o => o.SortOrder))
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<(List<EventCustomPropertyDefinition> Items, int TotalCount)> GetDefinitionsForEventPaged(
        Guid eventId,
        int pageNumber,
        int pageSize)
    {
        var query = _dbContext.EventCustomPropertyDefinitions
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Where(x => x.EventId == eventId)
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

    public async Task<List<EventCustomPropertyDefinition>> GetAllDefinitionsForEvent(Guid eventId)
    {
        return await _dbContext.EventCustomPropertyDefinitions
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Where(x => x.EventId == eventId)
            .Include(x => x.DefaultOption)
            .Include(x => x.Options.OrderBy(o => o.SortOrder))
            .Include(x => x.Values.OrderBy(v => v.Ordinal))
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.DisplayName)
            .ToListAsync();
    }

    public async Task<List<EventCustomPropertyDefinition>> GetTrackedDefinitionsForEvent(Guid eventId, CancellationToken cancellationToken)
    {
        return await _dbContext.EventCustomPropertyDefinitions
            .Where(x => x.EventId == eventId)
            .Include(x => x.Options.OrderBy(o => o.SortOrder))
            .Include(x => x.Values.OrderBy(v => v.Ordinal))
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountDefinitionsForEvent(Guid eventId, CancellationToken cancellationToken)
    {
        return await _dbContext.EventCustomPropertyDefinitions
            .CountAsync(x => x.EventId == eventId, cancellationToken);
    }

    public async Task<bool> ExistsDefinitionKey(Guid eventId, string namespaceValue, string key, Guid? excludeDefinitionId = null)
    {
        return await _dbContext.EventCustomPropertyDefinitions
            .AnyAsync(x => x.EventId == eventId
                && x.Namespace == namespaceValue
                && x.Key == key
                && (!excludeDefinitionId.HasValue || x.Id != excludeDefinitionId.Value));
    }

    public async Task<EventCustomPropertyDefinition> CreateWithOptions(
        EventCustomPropertyDefinition definition,
        IReadOnlyCollection<EventCustomPropertyOption> options,
        Guid? defaultOptionId,
        CancellationToken cancellationToken)
    {
        await _dbContext.EventCustomPropertyDefinitions.AddAsync(definition, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (options.Count > 0)
        {
            await _dbContext.EventCustomPropertyOptions.AddRangeAsync(options, cancellationToken);
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

    public async Task<EventCustomPropertyDefinition> UpdateWithOptions(
        EventCustomPropertyDefinition definition,
        IReadOnlyCollection<EventCustomPropertyOption> options,
        Guid? defaultOptionId,
        CancellationToken cancellationToken)
    {
        var existingOptions = await _dbContext.EventCustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.EventCustomPropertyDefinitionId == definition.Id)
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
                existing.SourceTemplateOptionId = option.SourceTemplateOptionId;
                existing.SourceTemplateVersion = option.SourceTemplateVersion;
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

            await _dbContext.EventCustomPropertyOptions.AddAsync(option, cancellationToken);
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
        var definition = await _dbContext.EventCustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (definition == null)
        {
            return false;
        }

        var options = await _dbContext.EventCustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.EventCustomPropertyDefinitionId == id)
            .ToListAsync(cancellationToken);
        var values = await _dbContext.EventCustomPropertyValues
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.EventCustomPropertyDefinitionId == id)
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

        var projections = await _dbContext.EventCustomPropertyProjections
            .Where(x => x.EventCustomPropertyDefinitionId == id)
            .ToListAsync(cancellationToken);
        _dbContext.EventCustomPropertyProjections.RemoveRange(projections);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<CustomPropertyPurgeDependencySummary?> GetPurgeDependencies(Guid id, CancellationToken cancellationToken)
    {
        var definition = await _dbContext.EventCustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .AsNoTracking()
            .Select(x => new { x.Id, x.TenantId, x.SourceTemplateId, x.SourceTemplateDefinitionId })
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (definition is null)
        {
            return null;
        }

        var optionCount = await _dbContext.EventCustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .CountAsync(x => x.EventCustomPropertyDefinitionId == id, cancellationToken);
        var valueCount = await _dbContext.EventCustomPropertyValues
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .CountAsync(x => x.EventCustomPropertyDefinitionId == id, cancellationToken);
        var projectionCount = await _dbContext.EventCustomPropertyProjections
            .CountAsync(x => x.EventCustomPropertyDefinitionId == id, cancellationToken);
        var auditCount = await _dbContext.AuditLogs
            .CountAsync(x => x.EntityType == nameof(EventCustomPropertyDefinition) && x.EntityId == id.ToString(), cancellationToken);
        var syncProvenanceCount = definition.SourceTemplateId.HasValue || definition.SourceTemplateDefinitionId.HasValue ? 1 : 0;

        return new CustomPropertyPurgeDependencySummary(
            id,
            definition.TenantId,
            "event_custom_property_definition",
            optionCount,
            valueCount,
            projectionCount,
            auditCount,
            syncProvenanceCount);
    }

    public async Task<bool> PurgeDefinition(Guid id, CancellationToken cancellationToken)
    {
        var dependencies = await GetPurgeDependencies(id, cancellationToken);
        if (dependencies is null || dependencies.HasBlockingDependencies)
        {
            return false;
        }

        await _dbContext.EventCustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.DefaultOptionId, (Guid?)null), cancellationToken);

        await _dbContext.EventCustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.EventCustomPropertyDefinitionId == id)
            .ExecuteDeleteAsync(cancellationToken);

        var deleted = await _dbContext.EventCustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted > 0;
    }

    public async Task<List<EventCustomPropertyValue>> GetValuesForEvent(Guid eventId)
    {
        return await _dbContext.EventCustomPropertyValues
            .AsNoTrackingWithIdentityResolution()
            .Where(x => x.EventId == eventId)
            .OrderBy(x => x.Ordinal)
            .ToListAsync();
    }

    public async Task<List<EventCustomPropertyValue>> GetValuesForDefinition(Guid definitionId)
    {
        return await _dbContext.EventCustomPropertyValues
            .AsNoTrackingWithIdentityResolution()
            .Where(x => x.EventCustomPropertyDefinitionId == definitionId)
            .OrderBy(x => x.Ordinal)
            .ToListAsync();
    }

    public async Task<EventCustomPropertyValue> SetValue(EventCustomPropertyValue value, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.EventCustomPropertyValues
            .FirstOrDefaultAsync(x =>
                x.EventCustomPropertyDefinitionId == value.EventCustomPropertyDefinitionId
                && x.EventId == value.EventId
                && x.Ordinal == value.Ordinal,
                cancellationToken);

        if (existing != null)
        {
            existing.TextValue = value.TextValue;
            existing.NumberValue = value.NumberValue;
            existing.BooleanValue = value.BooleanValue;
            existing.DateTimeValue = value.DateTimeValue;
            existing.OptionId = value.OptionId;
            existing.UpdatedBy = value.UpdatedBy;
            existing.UpdatedAt = value.UpdatedAt;
            _dbContext.Entry(existing).State = EntityState.Modified;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return existing;
        }

        await _dbContext.EventCustomPropertyValues.AddAsync(value, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return value;
    }

    public async Task<EventCustomPropertyOption> CreateOption(EventCustomPropertyOption option, CancellationToken cancellationToken)
    {
        await _dbContext.EventCustomPropertyOptions.AddAsync(option, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return option;
    }

    public async Task UpdateOption(EventCustomPropertyOption option, CancellationToken cancellationToken)
    {
        _dbContext.Entry(option).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SetMultiValues(
        Guid definitionId,
        Guid eventId,
        IReadOnlyCollection<EventCustomPropertyValue> values,
        CancellationToken cancellationToken)
    {
        await _dbContext.EventCustomPropertyValues
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.EventCustomPropertyDefinitionId == definitionId && x.EventId == eventId)
            .ExecuteDeleteAsync(cancellationToken);

        if (values.Count > 0)
        {
            await _dbContext.EventCustomPropertyValues.AddRangeAsync(values, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
