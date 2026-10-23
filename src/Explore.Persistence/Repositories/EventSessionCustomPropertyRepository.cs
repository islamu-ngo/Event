using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public class EventSessionCustomPropertyRepository : GenericRepository<EventSessionCustomPropertyDefinition, Guid>, IEventSessionCustomPropertyRepository
{
    private readonly ExploreDbContext _dbContext;

    public EventSessionCustomPropertyRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EventSession?> GetParentForMutationAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        Guid tenantId = RequireMutationTenant();
        var selected = await _dbContext.EventSessions.AsNoTracking().SingleOrDefaultAsync(
            row => row.Id == sessionId && row.TenantId == tenantId, cancellationToken);
        if (selected is null)
            return null;
        await RelationalEntityRowFence.AcquireAsync<Event>(
            _dbContext, tenantId, row => row.Id, selected.EventId, cancellationToken);
        await RelationalEntityRowFence.AcquireAsync<EventSession>(
            _dbContext, tenantId, row => row.Id, sessionId, cancellationToken);
        return await _dbContext.EventSessions.AsNoTracking().SingleOrDefaultAsync(
            row => row.Id == sessionId && row.TenantId == tenantId
                && row.EventId == selected.EventId && !row.Event.IsDeleted, cancellationToken);
    }

    private Guid RequireMutationTenant()
    {
        if (_dbContext.TenantFilterTenantId is not { } tenantId || tenantId == Guid.Empty
            || _dbContext.IsTenantFilterBypassed
            || _dbContext.Database.IsRelational()
                && _dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Definition mutation requires an exact tenant and a Serializable transaction.");
        return tenantId;
    }

    public async Task<EventSessionCustomPropertyDefinition?> GetDefinitionWithDetails(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.EventSessionCustomPropertyDefinitions
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Include(x => x.DefaultOption)
            .Include(x => x.Options.OrderBy(o => o.SortOrder))
            .Include(x => x.Values.OrderBy(v => v.Ordinal))
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<EventSessionCustomPropertyDefinition?> GetTrackedDefinitionWithOptions(Guid id, CancellationToken cancellationToken)
    {
        Guid tenantId = RequireMutationTenant();
        var selected = await _dbContext.EventSessionCustomPropertyDefinitions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == id && row.TenantId == tenantId, cancellationToken);
        if (selected is null)
            return null;
        var session = await GetParentForMutationAsync(selected.EventSessionId, cancellationToken);
        if (session is null)
            return null;
        await RelationalEntityRowFence.AcquireAsync<EventSessionCustomPropertyDefinition>(
            _dbContext, tenantId, row => row.Id, id, cancellationToken);
        var optionIds = await _dbContext.EventSessionCustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AsNoTracking()
            .Where(row => row.EventSessionCustomPropertyDefinitionId == id)
            .OrderBy(row => row.Id).Select(row => row.Id).ToListAsync(cancellationToken);
        foreach (Guid optionId in optionIds)
            await RelationalEntityRowFence.AcquireGlobalAsync<EventSessionCustomPropertyOption>(
                _dbContext, optionId, cancellationToken);
        var persisted = await _dbContext.EventSessionCustomPropertyDefinitions.AsNoTracking()
            .Include(row => row.Options)
            .SingleOrDefaultAsync(row => row.Id == id && row.TenantId == tenantId
                && row.EventSessionId == session.Id, cancellationToken);
        if (persisted is null)
            return null;
        var tracked = await _dbContext.EventSessionCustomPropertyDefinitions
            .Include(x => x.Options.OrderBy(o => o.SortOrder))
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (tracked is null)
            return null;
        RequireStoredScalars(tracked, persisted);
        var persistedOptions = persisted.Options.ToDictionary(option => option.Id);
        if (tracked.Options.Count != persistedOptions.Count)
            throw new InvalidOperationException("Tracked options do not match the persisted definition.");
        foreach (var option in tracked.Options)
        {
            if (!persistedOptions.TryGetValue(option.Id, out var storedOption))
                throw new InvalidOperationException("Tracked options do not match the persisted definition.");
            RequireStoredScalars(option, storedOption);
        }
        return tracked;
    }

    private void RequireStoredScalars<TEntity>(TEntity tracked, TEntity persisted) where TEntity : class
    {
        var persistedValues = _dbContext.Entry(persisted).CurrentValues;
        foreach (var property in _dbContext.Entry(tracked).Properties)
        {
            if (property.Metadata.Name is nameof(EventSessionCustomPropertyDefinition.CreatedAt)
                or nameof(EventSessionCustomPropertyDefinition.UpdatedAt) or nameof(EventSessionCustomPropertyDefinition.DeletedAt))
            {
                property.CurrentValue = persistedValues[property.Metadata.Name];
                property.OriginalValue = property.CurrentValue;
                property.IsModified = false;
            }
            else if (!Equals(property.CurrentValue, persistedValues[property.Metadata.Name]))
                throw new InvalidOperationException("Tracked definition values do not match the persisted mutation target.");
        }
    }

    public async Task<(List<EventSessionCustomPropertyDefinition> Items, int TotalCount)> GetDefinitionsForSessionPaged(
        Guid eventSessionId,
        int pageNumber,
        int pageSize)
    {
        var query = _dbContext.EventSessionCustomPropertyDefinitions
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Where(x => x.EventSessionId == eventSessionId)
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

    public async Task<List<EventSessionCustomPropertyDefinition>> GetAllDefinitionsForSession(Guid eventSessionId)
    {
        return await _dbContext.EventSessionCustomPropertyDefinitions
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Where(x => x.EventSessionId == eventSessionId)
            .Include(x => x.DefaultOption)
            .Include(x => x.Options.OrderBy(o => o.SortOrder))
            .Include(x => x.Values.OrderBy(v => v.Ordinal))
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.DisplayName)
            .ToListAsync();
    }

    public async Task<List<EventSessionCustomPropertyDefinition>> GetTrackedDefinitionsForSession(Guid eventSessionId, CancellationToken cancellationToken)
    {
        return await _dbContext.EventSessionCustomPropertyDefinitions
            .Where(x => x.EventSessionId == eventSessionId)
            .Include(x => x.Options.OrderBy(o => o.SortOrder))
            .Include(x => x.Values.OrderBy(v => v.Ordinal))
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountDefinitionsForSession(Guid eventSessionId, CancellationToken cancellationToken)
    {
        return await _dbContext.EventSessionCustomPropertyDefinitions
            .CountAsync(x => x.EventSessionId == eventSessionId, cancellationToken);
    }

    public async Task<bool> ExistsDefinitionKey(Guid eventSessionId, string namespaceValue, string key, Guid? excludeDefinitionId = null)
    {
        return await _dbContext.EventSessionCustomPropertyDefinitions
            .AnyAsync(x => x.EventSessionId == eventSessionId
                && x.Namespace == namespaceValue
                && x.Key == key
                && (!excludeDefinitionId.HasValue || x.Id != excludeDefinitionId.Value));
    }

    public async Task<EventSessionCustomPropertyDefinition> CreateWithOptions(
        EventSessionCustomPropertyDefinition definition,
        IReadOnlyCollection<EventSessionCustomPropertyOption> options,
        Guid? defaultOptionId,
        CancellationToken cancellationToken)
    {
        await _dbContext.EventSessionCustomPropertyDefinitions.AddAsync(definition, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (options.Count > 0)
        {
            await _dbContext.EventSessionCustomPropertyOptions.AddRangeAsync(options, cancellationToken);
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

    public async Task<EventSessionCustomPropertyDefinition> UpdateWithOptions(
        EventSessionCustomPropertyDefinition definition,
        IReadOnlyCollection<EventSessionCustomPropertyOption> options,
        Guid? defaultOptionId,
        CancellationToken cancellationToken)
    {
        var existingOptions = await _dbContext.EventSessionCustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.EventSessionCustomPropertyDefinitionId == definition.Id)
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

            await _dbContext.EventSessionCustomPropertyOptions.AddAsync(option, cancellationToken);
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
        var definition = await _dbContext.EventSessionCustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (definition == null)
        {
            return false;
        }

        var options = await _dbContext.EventSessionCustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.EventSessionCustomPropertyDefinitionId == id)
            .ToListAsync(cancellationToken);
        var values = await _dbContext.EventSessionCustomPropertyValues
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.EventSessionCustomPropertyDefinitionId == id)
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

        var projections = await _dbContext.EventSessionCustomPropertyProjections
            .Where(x => x.EventSessionCustomPropertyDefinitionId == id)
            .ToListAsync(cancellationToken);
        _dbContext.EventSessionCustomPropertyProjections.RemoveRange(projections);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> FenceDefinitionForPurgeAsync(Guid id, CancellationToken cancellationToken)
    {
        Guid tenantId = RequireMutationTenant();
        await RelationalEntityRowFence.AcquireAsync<EventSessionCustomPropertyDefinition>(
            _dbContext, tenantId, row => row.Id, id, cancellationToken);
        return await _dbContext.EventSessionCustomPropertyDefinitions.IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .AsNoTracking().AnyAsync(row => row.Id == id && row.TenantId == tenantId, cancellationToken);
    }

    public async Task<CustomPropertyPurgeDependencySummary?> GetPurgeDependencies(Guid id, CancellationToken cancellationToken)
    {
        var definition = await _dbContext.EventSessionCustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .AsNoTracking()
            .Select(x => new { x.Id, x.TenantId, x.SourceTemplateId, x.SourceTemplateDefinitionId })
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (definition is null)
        {
            return null;
        }

        var optionCount = await _dbContext.EventSessionCustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .CountAsync(x => x.EventSessionCustomPropertyDefinitionId == id, cancellationToken);
        var valueCount = await _dbContext.EventSessionCustomPropertyValues
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .CountAsync(x => x.EventSessionCustomPropertyDefinitionId == id, cancellationToken);
        var projectionCount = await _dbContext.EventSessionCustomPropertyProjections
            .CountAsync(x => x.EventSessionCustomPropertyDefinitionId == id, cancellationToken);
        var auditCount = await _dbContext.AuditLogs
            .CountAsync(x => x.EntityType == nameof(EventSessionCustomPropertyDefinition) && x.EntityId == id.ToString(), cancellationToken);
        var syncProvenanceCount = definition.SourceTemplateId.HasValue || definition.SourceTemplateDefinitionId.HasValue ? 1 : 0;

        return new CustomPropertyPurgeDependencySummary(
            id,
            definition.TenantId,
            "event_session_custom_property_definition",
            optionCount,
            valueCount,
            projectionCount,
            auditCount,
            syncProvenanceCount);
    }

    public async Task<bool> PurgeDefinition(Guid id, CancellationToken cancellationToken)
        => await _dbContext.ExecuteDisclosureMutationAsync(token => PurgeDefinitionCoreAsync(id, token), cancellationToken);

    private async Task<bool> PurgeDefinitionCoreAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await FenceDefinitionForPurgeAsync(id, cancellationToken))
            return false;
        await _dbContext.DisclosureMutations.EnlistQueryAsync(
            _dbContext.EventSessionCustomPropertyDefinitions.IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                .Where(value => value.Id == id).Select(value => value.TenantId), cancellationToken);
        var dependencies = await GetPurgeDependencies(id, cancellationToken);
        if (dependencies is null || dependencies.HasBlockingDependencies)
        {
            return false;
        }

        await _dbContext.EventSessionCustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.DefaultOptionId, (Guid?)null), cancellationToken);

        await _dbContext.EventSessionCustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.EventSessionCustomPropertyDefinitionId == id)
            .ExecuteDeleteAsync(cancellationToken);

        var deleted = await _dbContext.EventSessionCustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted > 0;
    }

    public async Task<List<EventSessionCustomPropertyValue>> GetValuesForSession(Guid eventSessionId)
    {
        return await _dbContext.EventSessionCustomPropertyValues
            .AsNoTrackingWithIdentityResolution()
            .Where(x => x.EventSessionId == eventSessionId)
            .OrderBy(x => x.Ordinal)
            .ToListAsync();
    }

    public async Task<List<EventSessionCustomPropertyValue>> GetValuesForDefinition(Guid definitionId)
    {
        return await _dbContext.EventSessionCustomPropertyValues
            .AsNoTrackingWithIdentityResolution()
            .Where(x => x.EventSessionCustomPropertyDefinitionId == definitionId)
            .OrderBy(x => x.Ordinal)
            .ToListAsync();
    }

    public async Task<EventSessionCustomPropertyValue> SetValue(EventSessionCustomPropertyValue value, CancellationToken cancellationToken)
    {
        Guid tenantId = RequireMutationTenant();
        if (value.TenantId != tenantId)
            throw new InvalidOperationException("Value mutation requires the exact execution tenant.");
        var selected = await _dbContext.EventSessionCustomPropertyValues.AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.EventSessionCustomPropertyDefinitionId == value.EventSessionCustomPropertyDefinitionId
                && x.EventSessionId == value.EventSessionId
                && x.Ordinal == value.Ordinal,
                cancellationToken);

        if (selected != null)
        {
            await RelationalEntityRowFence.AcquireAsync<EventSessionCustomPropertyValue>(
                _dbContext, tenantId, row => row.Id, selected.Id, cancellationToken);
            var persisted = await _dbContext.EventSessionCustomPropertyValues.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == selected.Id && row.TenantId == tenantId
                    && row.EventSessionCustomPropertyDefinitionId == value.EventSessionCustomPropertyDefinitionId
                    && row.EventSessionId == value.EventSessionId && row.Ordinal == value.Ordinal, cancellationToken)
                ?? throw new InvalidOperationException("The selected value mutation target changed.");
            var existing = await _dbContext.EventSessionCustomPropertyValues.SingleAsync(
                row => row.Id == persisted.Id, cancellationToken);
            RequireStoredScalars(existing, persisted);
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

        await _dbContext.EventSessionCustomPropertyValues.AddAsync(value, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return value;
    }

    public async Task<EventSessionCustomPropertyOption> CreateOption(EventSessionCustomPropertyOption option, CancellationToken cancellationToken)
    {
        await _dbContext.EventSessionCustomPropertyOptions.AddAsync(option, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return option;
    }

    public async Task UpdateOption(EventSessionCustomPropertyOption option, CancellationToken cancellationToken)
    {
        _dbContext.Entry(option).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SetMultiValues(
        Guid definitionId,
        Guid eventSessionId,
        IReadOnlyCollection<EventSessionCustomPropertyValue> values,
        CancellationToken cancellationToken)
        => await _dbContext.ExecuteDisclosureMutationAsync(
            token => SetMultiValuesCoreAsync(definitionId, eventSessionId, values, token), cancellationToken);

    private async Task SetMultiValuesCoreAsync(
        Guid definitionId, Guid eventSessionId, IReadOnlyCollection<EventSessionCustomPropertyValue> values,
        CancellationToken cancellationToken)
    {
        Guid tenantId = RequireMutationTenant();
        if (values.Any(value => value.TenantId != tenantId
            || value.EventSessionCustomPropertyDefinitionId != definitionId || value.EventSessionId != eventSessionId))
            throw new InvalidOperationException("Replacement values must retain the exact definition, parent and tenant.");
        await _dbContext.DisclosureMutations.EnlistQueryAsync(
            _dbContext.EventSessions.IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                .Where(value => value.Id == eventSessionId).Select(value => value.TenantId), cancellationToken);
        await _dbContext.EventSessionCustomPropertyValues
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.EventSessionCustomPropertyDefinitionId == definitionId && x.EventSessionId == eventSessionId)
            .ExecuteDeleteAsync(cancellationToken);

        if (values.Count > 0)
        {
            await _dbContext.EventSessionCustomPropertyValues.AddRangeAsync(values, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
