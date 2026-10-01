using System;
using System.Collections.Generic;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class TagTypeRepository : GenericRepository<TagType, int>, ITagTypeRepository
{
    private readonly ExploreDbContext _dbContext;

    public TagTypeRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TagType?> GetTagTypeWithDetails(int id)
    {
        return await _dbContext.TagTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<List<TagType>> GetTagTypesWithDetails()
    {
        return await _dbContext.TagTypes
            .AsNoTracking()
            .ToListAsync();
    }
}
