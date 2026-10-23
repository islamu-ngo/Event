using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public class EventSessionTemplateRepository : GenericRepository<EventSessionTemplate, Guid>, IEventSessionTemplateRepository
{
    private readonly ExploreDbContext _dbContext;

    public EventSessionTemplateRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EventSessionTemplate?> GetSessionTemplateWithDetails(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.EventSessionTemplates
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Include(x => x.EventTemplate)
            .Include(x => x.Definitions.OrderBy(d => d.SortOrder))
                .ThenInclude(d => d.DefaultOption)
            .Include(x => x.Definitions.OrderBy(d => d.SortOrder))
                .ThenInclude(d => d.Options.OrderBy(o => o.SortOrder))
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<EventSessionTemplate?> GetTrackedSessionTemplateWithDefinitions(Guid id, CancellationToken cancellationToken)
    {
        if (_dbContext.TenantFilterTenantId is not { } tenantId || tenantId == Guid.Empty
            || _dbContext.IsTenantFilterBypassed
            || _dbContext.Database.IsRelational()
                && _dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Session-template mutation requires an exact tenant and a Serializable transaction.");
        var selected = await _dbContext.EventSessionTemplates.AsNoTracking()
            .SingleOrDefaultAsync(template => template.Id == id && template.TenantId == tenantId, cancellationToken);
        if (selected is null)
            return null;
        await RelationalEntityRowFence.AcquireAsync<EventTemplate>(
            _dbContext, tenantId, template => template.Id, selected.EventTemplateId, cancellationToken);
        if (!await _dbContext.EventTemplates.AsNoTracking().AnyAsync(parent =>
                parent.Id == selected.EventTemplateId && parent.TenantId == tenantId && !parent.IsDeleted, cancellationToken))
            return null;
        await RelationalEntityRowFence.AcquireAsync<EventSessionTemplate>(
            _dbContext, tenantId, template => template.Id, id, cancellationToken);
        var persisted = await _dbContext.EventSessionTemplates.AsNoTracking()
            .SingleOrDefaultAsync(template => template.Id == id && template.TenantId == tenantId
                && template.EventTemplateId == selected.EventTemplateId, cancellationToken);
        if (persisted is null)
            return null;
        var tracked = await _dbContext.EventSessionTemplates
            .Include(x => x.Definitions.OrderBy(d => d.SortOrder))
                .ThenInclude(d => d.Options.OrderBy(o => o.SortOrder))
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (tracked is null)
            return null;
        var persistedValues = _dbContext.Entry(persisted).CurrentValues;
        foreach (var property in _dbContext.Entry(tracked).Properties)
        {
            if (property.Metadata.Name is nameof(EventSessionTemplate.CreatedAt)
                or nameof(EventSessionTemplate.UpdatedAt) or nameof(EventSessionTemplate.DeletedAt))
            {
                // Normalize database-owned audit facts without making a read
                // dirty merely because the provider reduced timestamp precision.
                property.CurrentValue = persistedValues[property.Metadata.Name];
                property.OriginalValue = property.CurrentValue;
                property.IsModified = false;
            }
            else if (!Equals(property.CurrentValue, persistedValues[property.Metadata.Name]))
                throw new InvalidOperationException("Tracked session-template values do not match the persisted mutation target.");
        }
        return tracked;
    }

    public async Task<(List<EventSessionTemplate> Items, int TotalCount)> GetSessionTemplatesPaged(
        Guid eventTemplateId,
        int pageNumber,
        int pageSize)
    {
        var query = _dbContext.EventSessionTemplates
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Where(x => x.EventTemplateId == eventTemplateId);

        var orderedQuery = query
            .Include(x => x.Definitions)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.DisplayName);

        var totalCount = await query.CountAsync();
        var items = await orderedQuery
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<bool> ExistsSessionTemplateKey(Guid eventTemplateId, string sessionTemplateKey, int version, Guid? excludeSessionTemplateId = null)
    {
        return await _dbContext.EventSessionTemplates
            .AnyAsync(x => x.EventTemplateId == eventTemplateId
                && x.SessionTemplateKey == sessionTemplateKey
                && x.Version == version
                && (!excludeSessionTemplateId.HasValue || x.Id != excludeSessionTemplateId.Value));
    }

    public async Task<EventSessionTemplate?> GetLatestPublishedSessionTemplate(Guid eventTemplateId, string sessionTemplateKey)
    {
        return await _dbContext.EventSessionTemplates
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Where(x => x.EventTemplateId == eventTemplateId
                && x.SessionTemplateKey == sessionTemplateKey
                && x.IsPublished
                && x.IsActive)
            .Include(x => x.Definitions.OrderBy(d => d.SortOrder))
                .ThenInclude(d => d.DefaultOption)
            .Include(x => x.Definitions.OrderBy(d => d.SortOrder))
                .ThenInclude(d => d.Options.OrderBy(o => o.SortOrder))
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync();
    }

    public async Task<EventSessionTemplate?> GetPublishedSessionTemplateVersion(
        Guid eventTemplateId,
        string sessionTemplateKey,
        int version,
        CancellationToken cancellationToken)
    {
        return await _dbContext.EventSessionTemplates
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Where(x => x.EventTemplateId == eventTemplateId
                && x.SessionTemplateKey == sessionTemplateKey
                && x.Version == version
                && x.IsPublished
                && x.IsActive)
            .Include(x => x.Definitions.OrderBy(d => d.SortOrder))
                .ThenInclude(d => d.DefaultOption)
            .Include(x => x.Definitions.OrderBy(d => d.SortOrder))
                .ThenInclude(d => d.Options.OrderBy(o => o.SortOrder))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<EventSessionTemplate> CreateWithDefinitions(
        EventSessionTemplate sessionTemplate,
        IReadOnlyCollection<SessionTemplateDefinitionWithOptions> definitionsWithOptions,
        CancellationToken cancellationToken)
    {
        await _dbContext.EventSessionTemplates.AddAsync(sessionTemplate, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var defWithOptions in definitionsWithOptions)
        {
            defWithOptions.Definition.EventSessionTemplateId = sessionTemplate.Id;
            defWithOptions.Definition.EventSessionTemplate = sessionTemplate;
            defWithOptions.Definition.TenantId = sessionTemplate.TenantId;
            await _dbContext.EventSessionTemplateCustomPropertyDefinitions.AddAsync(defWithOptions.Definition, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (defWithOptions.Options.Count > 0)
            {
                foreach (var option in defWithOptions.Options)
                {
                    option.EventSessionTemplateCustomPropertyDefinitionId = defWithOptions.Definition.Id;
                    option.Definition = defWithOptions.Definition;
                }
                await _dbContext.EventSessionTemplateCustomPropertyOptions.AddRangeAsync(defWithOptions.Options, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            if (defWithOptions.DefaultOptionId.HasValue)
            {
                defWithOptions.Definition.DefaultOptionId = defWithOptions.DefaultOptionId;
                _dbContext.Entry(defWithOptions.Definition).State = EntityState.Modified;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        return await GetSessionTemplateWithDetails(sessionTemplate.Id, cancellationToken) ?? sessionTemplate;
    }

    public async Task<EventSessionTemplate> UpdateWithDefinitions(
        EventSessionTemplate sessionTemplate,
        IReadOnlyCollection<SessionTemplateDefinitionWithOptions> definitionsWithOptions,
        CancellationToken cancellationToken)
    {
        _dbContext.Entry(sessionTemplate).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var existingDefinitionIds = await _dbContext.EventSessionTemplateCustomPropertyDefinitions
            .Where(x => x.EventSessionTemplateId == sessionTemplate.Id)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var defId in existingDefinitionIds)
        {
            await _dbContext.EventSessionTemplateCustomPropertyDefinitions
                .Where(x => x.Id == defId)
                .ExecuteUpdateAsync(x => x.SetProperty(d => d.DefaultOptionId, (Guid?)null), cancellationToken);
        }

        await _dbContext.EventSessionTemplateCustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => existingDefinitionIds.Contains(x.EventSessionTemplateCustomPropertyDefinitionId))
            .ExecuteDeleteAsync(cancellationToken);

        await _dbContext.EventSessionTemplateCustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.EventSessionTemplateId == sessionTemplate.Id)
            .ExecuteDeleteAsync(cancellationToken);

        foreach (var defWithOptions in definitionsWithOptions)
        {
            defWithOptions.Definition.EventSessionTemplateId = sessionTemplate.Id;
            defWithOptions.Definition.EventSessionTemplate = sessionTemplate;
            defWithOptions.Definition.TenantId = sessionTemplate.TenantId;
            await _dbContext.EventSessionTemplateCustomPropertyDefinitions.AddAsync(defWithOptions.Definition, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (defWithOptions.Options.Count > 0)
            {
                foreach (var option in defWithOptions.Options)
                {
                    option.EventSessionTemplateCustomPropertyDefinitionId = defWithOptions.Definition.Id;
                    option.Definition = defWithOptions.Definition;
                }
                await _dbContext.EventSessionTemplateCustomPropertyOptions.AddRangeAsync(defWithOptions.Options, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            if (defWithOptions.DefaultOptionId.HasValue)
            {
                defWithOptions.Definition.DefaultOptionId = defWithOptions.DefaultOptionId;
                _dbContext.Entry(defWithOptions.Definition).State = EntityState.Modified;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        return await GetSessionTemplateWithDetails(sessionTemplate.Id, cancellationToken) ?? sessionTemplate;
    }

    public async Task<bool> DeleteSessionTemplate(Guid id, CancellationToken cancellationToken)
    {
        var definitionIds = await _dbContext.EventSessionTemplateCustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.EventSessionTemplateId == id)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (definitionIds.Count > 0)
        {
            await _dbContext.EventSessionTemplateCustomPropertyOptions
                .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                .Where(x => definitionIds.Contains(x.EventSessionTemplateCustomPropertyDefinitionId))
                .ExecuteDeleteAsync(cancellationToken);

            await _dbContext.EventSessionTemplateCustomPropertyDefinitions
                .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                .Where(x => x.EventSessionTemplateId == id)
                .ExecuteDeleteAsync(cancellationToken);
        }

        var affectedRows = await _dbContext.EventSessionTemplates
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

        return affectedRows > 0;
    }
}
