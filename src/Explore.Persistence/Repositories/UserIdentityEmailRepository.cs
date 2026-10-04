using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class UserIdentityEmailRepository(ExploreDbContext context) : IUserIdentityEmailRepository
{
    public Task<UserIdentityEmailClaim?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        context.UserIdentityEmailClaims.AsNoTracking()
            .SingleOrDefaultAsync(claim => claim.NormalizedEmail == normalizedEmail, cancellationToken);

    public async Task<IReadOnlyList<UserIdentityEmailClaim>> GetByUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await context.UserIdentityEmailClaims.AsNoTracking()
            .Where(claim => claim.UserId == userId
                && context.UserIdentityEmailEvidence.Any(evidence => evidence.ClaimId == claim.Id && evidence.IsActive))
            .OrderBy(claim => claim.Id)
            .ToArrayAsync(cancellationToken);

    public Task<UserIdentityEmailEvidence?> GetEvidenceByBindingAsync(Guid externalLoginId, CancellationToken cancellationToken) =>
        context.UserIdentityEmailEvidence.AsNoTracking()
            .SingleOrDefaultAsync(evidence => evidence.ExternalLoginId == externalLoginId, cancellationToken);

    public async Task<UserIdentityEmailClaim> CreateClaimAsync(UserIdentityEmailClaim claim, CancellationToken cancellationToken)
    {
        RequireTransaction();
        await context.UserIdentityEmailClaims.AddAsync(claim, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return claim;
    }

    public async Task<UserIdentityEmailEvidence> CreateEvidenceAsync(UserIdentityEmailEvidence evidence, CancellationToken cancellationToken)
    {
        RequireTransaction();
        await context.UserIdentityEmailEvidence.AddAsync(evidence, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return evidence;
    }

    public async Task RemoveEvidenceByBindingAsync(Guid externalLoginId, CancellationToken cancellationToken)
    {
        RequireTransaction();
        UserIdentityEmailEvidence? evidence = await context.UserIdentityEmailEvidence
            .SingleOrDefaultAsync(value => value.ExternalLoginId == externalLoginId, cancellationToken);
        if (evidence is null)
            return;

        context.UserIdentityEmailEvidence.Remove(evidence);
        await context.SaveChangesAsync(cancellationToken);
        if (!await context.UserIdentityEmailEvidence.AnyAsync(
                value => value.ClaimId == evidence.ClaimId && value.IsActive, cancellationToken))
        {
            UserIdentityEmailClaim claim = await context.UserIdentityEmailClaims
                .SingleAsync(value => value.Id == evidence.ClaimId, cancellationToken);
            context.UserIdentityEmailClaims.Remove(claim);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private void RequireTransaction()
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Identity email ownership requires an account transaction.");
    }
}
