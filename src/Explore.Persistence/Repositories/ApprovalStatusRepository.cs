using System;
using System.Collections.Generic;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class ApprovalStatusRepository : GenericRepository<ApprovalStatus, int>, IApprovalStatusRepository
{
    private readonly ExploreDbContext _dbContext;
    public ApprovalStatusRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }
    public async Task<List<ApprovalStatus>> GetStatusTypesWithDetails()
    {
        var statusTypes = await _dbContext.ApprovalStatuses
            .AsNoTracking()
            .ToListAsync();
        return statusTypes;
    }
    public async Task<ApprovalStatus?> GetStatusTypeWithDetails(int id)
    {
        var statusType = await _dbContext.ApprovalStatuses
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id);
        return statusType;
    }
}
