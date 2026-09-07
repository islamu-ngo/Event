using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class EventSessionLanguageRepository : GenericRepository<EventSessionLanguage, int>, IEventSessionLanguageRepository
{
    private readonly ExploreDbContext _dbContext;

    public EventSessionLanguageRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<EventSessionLanguage>> GetBySession(Guid eventSessionId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.EventSessionLanguages
            .AsNoTracking()
            .Include(l => l.Language)
            .Where(l => l.EventSessionId == eventSessionId)
            .ToListAsync(cancellationToken);
    }

    public async Task<EventSessionLanguage?> GetBySessionAndLanguage(
        Guid eventSessionId,
        int languageId,
        int? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.EventSessionLanguages
            .AsNoTracking()
            .Where(l => l.EventSessionId == eventSessionId && l.LanguageId == languageId);

        if (excludeId.HasValue)
        {
            query = query.Where(l => l.Id != excludeId.Value);
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<(List<EventSessionLanguage> Items, int TotalCount)> GetLanguagesWithDetailsPaged(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.EventSessionLanguages
            .AsNoTracking()
            .Include(l => l.Language)
            .Include(l => l.EventSession)
                .ThenInclude(s => s.Event)
            .OrderByDescending(l => l.Id);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
