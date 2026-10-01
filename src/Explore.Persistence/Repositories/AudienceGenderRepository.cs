using System;
using System.Collections.Generic;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class AudienceGenderRepository : GenericRepository<AudienceGender, int>, IAudienceGenderRepository
{
    private readonly ExploreDbContext _dbContext;
    public AudienceGenderRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<AudienceGender>> GetAudienceGendersWithDetails()
    {
        var audienceGenders = await _dbContext.AudienceGenders
            .AsNoTracking()
            .ToListAsync();
        return audienceGenders;
    }

    public async Task<AudienceGender?> GetAudienceGenderWithDetails(int id)
    {
        var audienceGender = await _dbContext.AudienceGenders
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id);
        return audienceGender;
    }
}
