using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class EventTemplateRepository : GenericRepository<EventTemplate, Guid>, IEventTemplateRepository
{
    private readonly ExploreDbContext _dbContext;

    public EventTemplateRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EventTemplate?> GetTemplateWithDetails(Guid id)
    {
        return await _dbContext.EventTemplates
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Include(x => x.EventType)
            .Include(x => x.Definitions.OrderBy(d => d.SortOrder))
                .ThenInclude(d => d.DefaultOption)
            .Include(x => x.Definitions.OrderBy(d => d.SortOrder))
                .ThenInclude(d => d.Options.OrderBy(o => o.SortOrder))
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<EventTemplate?> GetTrackedTemplateWithDefinitions(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.EventTemplates
            .Include(x => x.Definitions.OrderBy(d => d.SortOrder))
                .ThenInclude(d => d.Options.OrderBy(o => o.SortOrder))
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<(List<EventTemplate> Items, int TotalCount)> GetTemplatesPaged(
        Guid tenantId,
        int? eventTypeId,
        int pageNumber,
        int pageSize)
    {
        var query = _dbContext.EventTemplates
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Where(x => x.TenantId == tenantId);

        if (eventTypeId.HasValue)
            query = query.Where(x => x.EventTypeId == eventTypeId.Value);

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

    public async Task<bool> ExistsTemplateKey(Guid tenantId, string templateKey, int version, Guid? excludeTemplateId = null)
    {
        return await _dbContext.EventTemplates
            .AnyAsync(x => x.TenantId == tenantId
                && x.TemplateKey == templateKey
                && x.Version == version
                && (!excludeTemplateId.HasValue || x.Id != excludeTemplateId.Value));
    }

    public async Task<EventTemplate?> GetLatestPublishedTemplate(Guid tenantId, string templateKey)
    {
        return await _dbContext.EventTemplates
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Where(x => x.TenantId == tenantId
                && x.TemplateKey == templateKey
                && x.IsPublished
                && x.IsActive)
            .Include(x => x.Definitions.OrderBy(d => d.SortOrder))
                .ThenInclude(d => d.DefaultOption)
            .Include(x => x.Definitions.OrderBy(d => d.SortOrder))
                .ThenInclude(d => d.Options.OrderBy(o => o.SortOrder))
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync();
    }

    public async Task<EventTemplate?> GetPublishedTemplateVersion(
        Guid tenantId,
        string templateKey,
        int version,
        CancellationToken cancellationToken)
    {
        return await _dbContext.EventTemplates
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Where(x => x.TenantId == tenantId
                && x.TemplateKey == templateKey
                && x.Version == version
                && x.IsPublished
                && x.IsActive)
            .Include(x => x.Definitions.OrderBy(d => d.SortOrder))
                .ThenInclude(d => d.DefaultOption)
            .Include(x => x.Definitions.OrderBy(d => d.SortOrder))
                .ThenInclude(d => d.Options.OrderBy(o => o.SortOrder))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<EventTemplate> CreateWithDefinitions(
        EventTemplate template,
        IReadOnlyCollection<TemplateDefinitionWithOptions> definitionsWithOptions,
        CancellationToken cancellationToken)
    {
        await _dbContext.EventTemplates.AddAsync(template, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var defWithOptions in definitionsWithOptions)
        {
            await _dbContext.EventTemplateCustomPropertyDefinitions.AddAsync(defWithOptions.Definition, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (defWithOptions.Options.Count > 0)
            {
                await _dbContext.EventTemplateCustomPropertyOptions.AddRangeAsync(defWithOptions.Options, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            if (defWithOptions.DefaultOptionId.HasValue)
            {
                defWithOptions.Definition.DefaultOptionId = defWithOptions.DefaultOptionId;
                _dbContext.Entry(defWithOptions.Definition).State = EntityState.Modified;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        return await GetTemplateWithDetails(template.Id) ?? template;
    }

    public async Task<EventTemplate> UpdateWithDefinitions(
        EventTemplate template,
        IReadOnlyCollection<TemplateDefinitionWithOptions> definitionsWithOptions,
        CancellationToken cancellationToken)
    {
        _dbContext.Entry(template).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var existingDefinitionIds = await _dbContext.EventTemplateCustomPropertyDefinitions
            .Where(x => x.EventTemplateId == template.Id)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var defId in existingDefinitionIds)
        {
            await _dbContext.EventTemplateCustomPropertyDefinitions
                .Where(x => x.Id == defId)
                .ExecuteUpdateAsync(x => x.SetProperty(d => d.DefaultOptionId, (Guid?)null), cancellationToken);
        }

        await _dbContext.EventTemplateCustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => existingDefinitionIds.Contains(x.EventTemplateCustomPropertyDefinitionId))
            .ExecuteDeleteAsync(cancellationToken);

        await _dbContext.EventTemplateCustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.EventTemplateId == template.Id)
            .ExecuteDeleteAsync(cancellationToken);

        foreach (var defWithOptions in definitionsWithOptions)
        {
            await _dbContext.EventTemplateCustomPropertyDefinitions.AddAsync(defWithOptions.Definition, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (defWithOptions.Options.Count > 0)
            {
                await _dbContext.EventTemplateCustomPropertyOptions.AddRangeAsync(defWithOptions.Options, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            if (defWithOptions.DefaultOptionId.HasValue)
            {
                defWithOptions.Definition.DefaultOptionId = defWithOptions.DefaultOptionId;
                _dbContext.Entry(defWithOptions.Definition).State = EntityState.Modified;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        return await GetTemplateWithDetails(template.Id) ?? template;
    }

    public async Task<bool> DeleteTemplate(Guid id, CancellationToken cancellationToken)
    {
        var definitionIds = await _dbContext.EventTemplateCustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.EventTemplateId == id)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (definitionIds.Count > 0)
        {
            await _dbContext.EventTemplateCustomPropertyOptions
                .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                .Where(x => definitionIds.Contains(x.EventTemplateCustomPropertyDefinitionId))
                .ExecuteDeleteAsync(cancellationToken);

            await _dbContext.EventTemplateCustomPropertyDefinitions
                .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                .Where(x => x.EventTemplateId == id)
                .ExecuteDeleteAsync(cancellationToken);
        }

        var affectedRows = await _dbContext.EventTemplates
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

        return affectedRows > 0;
    }
}
