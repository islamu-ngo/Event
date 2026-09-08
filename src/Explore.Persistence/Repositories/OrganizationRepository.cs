using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class OrganizationRepository : GenericRepository<Organization, Guid>, IOrganizationRepository
{
    private readonly ExploreDbContext _dbContext;

    public OrganizationRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public new async Task<Organization?> GetById(Guid id)
    {
        return await _dbContext.Organizations
            .Include(o => o.Pii)
            .Include(o => o.TenantParticipations)
                .ThenInclude(p => p.ApprovalStatus)
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<List<Organization>> GetOrganizationsWithDetails(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Organizations
            .AsNoTracking()
            .AsSplitQuery()
            .Include(o => o.Pii)
            .Include(o => o.Actor)
                .ThenInclude(a => a!.Pii)
            .Include(o => o.Actor)
                .ThenInclude(a => a!.AtprotoIdentities)
            .Include(o => o.TenantParticipations)
                .ThenInclude(p => p.ApprovalStatus)
            .Include(o => o.TenantParticipations)
                .ThenInclude(p => p.Tenant)
            .ToListAsync(cancellationToken);
    }

    public async Task<Organization?> GetOrganizationWithDetails(Guid id, CancellationToken cancellationToken = default)
    {
        return await OrganizationDetailsQuery()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task<Organization?> GetOrganizationWithDetailsByActorId(
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        return await OrganizationDetailsQuery()
            .FirstOrDefaultAsync(o => o.Actor != null && o.Actor.Id == actorId, cancellationToken);
    }

    private IQueryable<Organization> OrganizationDetailsQuery()
    {
        return _dbContext.Organizations
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Include(o => o.Pii)
            .Include(o => o.Actor)
                .ThenInclude(a => a!.Pii)
            .Include(o => o.Actor)
                .ThenInclude(a => a!.AtprotoIdentities)
            .Include(o => o.TenantParticipations)
                .ThenInclude(p => p.ApprovalStatus)
            .Include(o => o.TenantParticipations)
                .ThenInclude(p => p.Tenant)
            .Include(o => o.TenantParticipations)
                .ThenInclude(p => p.Members)
                .ThenInclude(m => m.User)
            .Include(o => o.TenantParticipations)
                .ThenInclude(p => p.Members)
                .ThenInclude(m => m.User)
                .ThenInclude(u => u!.Pii)
            .Include(o => o.TenantParticipations)
                .ThenInclude(p => p.Members)
                .ThenInclude(m => m.Role)
            .Include(o => o.TenantParticipations)
                .ThenInclude(p => p.Members)
                .ThenInclude(m => m.OrganizationPosition);
    }

    public async Task<List<Organization>> GetMyOrganizations(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Organizations
            .AsNoTracking()
            .AsSplitQuery()
            .Include(o => o.Pii)
            .Include(o => o.Actor)
                .ThenInclude(a => a!.Pii)
            .Include(o => o.Actor)
                .ThenInclude(a => a!.AtprotoIdentities)
            .Include(o => o.TenantParticipations)
                .ThenInclude(p => p.ApprovalStatus)
            .Include(o => o.TenantParticipations)
                .ThenInclude(p => p.Members)
                    .ThenInclude(m => m.Role)
            .Where(o => o.TenantParticipations.Any(p => p.Members.Any(m => m.UserId == userId)))
            .ToListAsync(cancellationToken);
    }

    public async Task<(List<Organization> Items, int TotalCount)> GetOrganizationsWithDetailsPaged(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Organizations
            .AsNoTracking()
            .AsSplitQuery()
            .Include(o => o.Pii)
            .Include(o => o.Actor)
                .ThenInclude(a => a!.Pii)
            .Include(o => o.Actor)
                .ThenInclude(a => a!.AtprotoIdentities)
            .Include(o => o.TenantParticipations)
                .ThenInclude(p => p.ApprovalStatus)
            .Include(o => o.TenantParticipations)
                .ThenInclude(p => p.Tenant)
            .OrderBy(o => o.Pii != null ? o.Pii.FullName : string.Empty);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<(List<Organization> Items, int TotalCount)> GetMyOrganizationsPaged(
        Guid userId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Organizations
            .AsNoTracking()
            .AsSplitQuery()
            .Include(o => o.Pii)
            .Include(o => o.Actor)
                .ThenInclude(a => a!.Pii)
            .Include(o => o.Actor)
                .ThenInclude(a => a!.AtprotoIdentities)
            .Include(o => o.TenantParticipations)
                .ThenInclude(p => p.ApprovalStatus)
            .Include(o => o.TenantParticipations)
                .ThenInclude(p => p.Members)
                    .ThenInclude(m => m.Role)
            .Where(o => o.TenantParticipations.Any(p => p.Members.Any(m => m.UserId == userId)))
            .OrderBy(o => o.Pii != null ? o.Pii.FullName : string.Empty);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<int> ForgetPiiAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.OrganizationPii
            .Where(p => p.OrganizationId == organizationId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
