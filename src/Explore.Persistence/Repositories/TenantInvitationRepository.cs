using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class TenantInvitationRepository : GenericRepository<TenantInvitation, Guid>, ITenantInvitationRepository
{
    private readonly ExploreDbContext _dbContext;

    public TenantInvitationRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TenantInvitation?> GetByTokenAsync(string token)
    {
        return await _dbContext.TenantInvitations
            .AsNoTracking()
            .Include(i => i.Role)
            .Include(i => i.Tenant)
            .FirstOrDefaultAsync(i => i.Token == token && !i.IsAccepted);
    }

    public async Task<List<TenantInvitation>> GetPendingByEmailAsync(Guid tenantId, string email)
    {
        return await _dbContext.TenantInvitations
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId
                        && i.Email == email
                        && !i.IsAccepted
                        && i.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> ExistsActiveAsync(Guid tenantId, string email)
    {
        return await _dbContext.TenantInvitations
            .AsNoTracking()
            .AnyAsync(i => i.TenantId == tenantId
                           && i.Email == email
                           && !i.IsAccepted
                           && i.ExpiresAt > DateTime.UtcNow);
    }
}
