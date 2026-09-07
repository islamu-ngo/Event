using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed class AiConsentGrantRepository : GenericRepository<AiConsentGrant, Guid>, IAiConsentGrantRepository
{
    private readonly ExploreDbContext _context;

    public AiConsentGrantRepository(ExploreDbContext context) : base(context)
    {
        _context = context;
    }

    public Task<AiConsentGrant?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _context.AiConsentGrants.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

    public Task<AiConsentGrant?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        _context.AiConsentGrants.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

    public async Task<IReadOnlyList<AiConsentGrant>> ListForUserAsync(Guid subjectUserId, CancellationToken cancellationToken) =>
        await _context.AiConsentGrants.AsNoTracking()
            .Where(g => g.SubjectUserId == subjectUserId)
            .OrderByDescending(g => g.GrantedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AiConsentGrant>> ListGrantedForSubjectAsync(
        Guid subjectUserId,
        string entityName,
        string fieldName,
        int providerTrustTierId,
        CancellationToken cancellationToken) =>
        await _context.AiConsentGrants.AsNoTracking()
            .Where(g => g.SubjectUserId == subjectUserId
                && g.EntityName == entityName
                && g.FieldName == fieldName
                && g.ProviderTrustTierId == providerTrustTierId
                && g.StatusId == (int)AiConsentGrantStatusEnum.Granted
                && g.RevokedAtUtc == null
                && (g.ExpiresAtUtc == null || g.ExpiresAtUtc > DateTimeOffset.UtcNow))
            .OrderByDescending(g => g.GrantedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<AiConsentGrant?> FindActiveGrantAsync(
        Guid subjectUserId,
        string entityName,
        string fieldName,
        int providerTrustTierId,
        CancellationToken cancellationToken) =>
        await _context.AiConsentGrants.AsNoTracking()
            .Where(g => g.SubjectUserId == subjectUserId
                && g.EntityName == entityName
                && g.FieldName == fieldName
                && g.ProviderTrustTierId == providerTrustTierId
                && g.StatusId == (int)AiConsentGrantStatusEnum.Granted
                && g.RevokedAtUtc == null
                && (g.ExpiresAtUtc == null || g.ExpiresAtUtc > DateTimeOffset.UtcNow))
            .OrderByDescending(g => g.GrantedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddAsync(AiConsentGrant grant, CancellationToken cancellationToken)
    {
        await _context.AiConsentGrants.AddAsync(grant, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(AiConsentGrant grant, CancellationToken cancellationToken)
    {
        _context.AiConsentGrants.Update(grant);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAsync(Guid grantId, Guid revokedByUserId, DateTime revokedAtUtc, CancellationToken cancellationToken)
    {
        var grant = await _context.AiConsentGrants.FirstOrDefaultAsync(g => g.Id == grantId, cancellationToken);
        if (grant is null)
        {
            return;
        }

        grant.StatusId = (int)AiConsentGrantStatusEnum.Revoked;
        grant.RevokedAtUtc = revokedAtUtc;
        grant.UpdatedAt = DateTime.UtcNow;
        grant.UpdatedBy = revokedByUserId;

        await _context.SaveChangesAsync(cancellationToken);
    }
}
