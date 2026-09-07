using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class WebhookEventTypeRepository : IWebhookEventTypeRepository
{
    private readonly ExploreDbContext _dbContext;

    public WebhookEventTypeRepository(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<WebhookEventType> CreateAsync(WebhookEventType eventType, CancellationToken cancellationToken)
    {
        if (eventType.Id == Guid.Empty)
        {
            eventType.Id = Guid.CreateVersion7();
        }

        await _dbContext.WebhookEventTypes.AddAsync(eventType, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return eventType;
    }

    public async Task<WebhookEventType?> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        return await _dbContext.WebhookEventTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Name == name, cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookEventType>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await _dbContext.WebhookEventTypes
            .AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .OrderBy(e => e.GroupName)
            .ThenBy(e => e.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookEventType>> GetByNamesAsync(
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken)
    {
        if (names.Count == 0)
        {
            return [];
        }

        return await _dbContext.WebhookEventTypes
            .AsNoTracking()
            .Where(e => names.Contains(e.Name))
            .OrderBy(e => e.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookEventType>> GetEnabledAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.WebhookEventTypes
            .AsNoTracking()
            .Where(e => e.IsEnabled)
            .OrderBy(e => e.GroupName)
            .ThenBy(e => e.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateAsync(WebhookEventType eventType, CancellationToken cancellationToken)
    {
        _dbContext.WebhookEventTypes.Update(eventType);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
