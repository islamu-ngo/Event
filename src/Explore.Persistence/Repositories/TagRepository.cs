using System;
using System.Collections.Generic;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class TagRepository : GenericRepository<Tag, Guid>, ITagRepository
{
    private readonly ExploreDbContext _dbContext;

    public TagRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Tag?> GetTagWithDetails(Guid id)
    {
        return await _dbContext.Tags
            .AsNoTracking()
            .Include(t => t.Tenant)
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<List<Tag>> GetTagsWithDetails()
    {
        return await _dbContext.Tags
            .AsNoTracking()
            .Include(t => t.Tenant)
            .ToListAsync();
    }

    public async Task<(List<Tag> Items, int TotalCount)> GetTagsWithDetailsPaged(int pageNumber, int pageSize)
    {
        var query = _dbContext.Tags
            .AsNoTracking()
            .OrderBy(t => t.FullName);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }
}
