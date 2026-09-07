using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class EventSessionTemplateRepository : GenericRepository<EventSessionTemplate, Guid>, IEventSessionTemplateRepository
{
    private readonly ExploreDbContext _dbContext;

    public EventSessionTemplateRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EventSessionTemplate?> GetSessionTemplateWithDetails(Guid id)
    {
        return await _dbContext.EventSessionTemplates
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Include(x => x.EventTemplate)
            .Include(x => x.Definitions.OrderBy(d => d.SortOrder))
                .ThenInclude(d => d.DefaultOption)
            .Include(x => x.Definitions.OrderBy(d => d.SortOrder))
                .ThenInclude(d => d.Options.OrderBy(o => o.SortOrder))
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<EventSessionTemplate?> GetTrackedSessionTemplateWithDefinitions(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.EventSessionTemplates
            .Include(x => x.Definitions.OrderBy(d => d.SortOrder))
                .ThenInclude(d => d.Options.OrderBy(o => o.SortOrder))
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
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
            await _dbContext.EventSessionTemplateCustomPropertyDefinitions.AddAsync(defWithOptions.Definition, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (defWithOptions.Options.Count > 0)
            {
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

        return await GetSessionTemplateWithDetails(sessionTemplate.Id) ?? sessionTemplate;
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
            await _dbContext.EventSessionTemplateCustomPropertyDefinitions.AddAsync(defWithOptions.Definition, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (defWithOptions.Options.Count > 0)
            {
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

        return await GetSessionTemplateWithDetails(sessionTemplate.Id) ?? sessionTemplate;
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
