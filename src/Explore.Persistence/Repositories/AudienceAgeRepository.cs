using System;
using System.Collections.Generic;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class AudienceAgeRepository : GenericRepository<AudienceAge, int>, IAudienceAgeRepository
{
    private readonly ExploreDbContext _dbContext;

    public AudienceAgeRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<AudienceAge>> GetAudienceAgesWithDetails()
    {
        var audienceAges = await _dbContext.AudienceAges
            .AsNoTracking()
            .ToListAsync();
        return audienceAges;
    }
    public async Task<AudienceAge?> GetAudienceAgeWithDetails(int id)
    {
        var audienceAge = await _dbContext.AudienceAges
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id);
        return audienceAge;
    }
}
