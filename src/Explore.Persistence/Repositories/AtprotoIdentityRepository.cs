using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class AtprotoIdentityRepository(ExploreDbContext dbContext)
    : GenericRepository<AtprotoIdentity, Guid>(dbContext), IAtprotoIdentityRepository
{
    public Task<AtprotoIdentity?> GetByDid(
        AtprotoDid did,
        CancellationToken cancellationToken = default) =>
        dbContext.AtprotoIdentities
            .Include(identity => identity.Actor)
                .ThenInclude(actor => actor.ExternalActorSubject)
            .Include(identity => identity.Actor)
                .ThenInclude(actor => actor.Organization)
            .Include(identity => identity.Actor)
                .ThenInclude(actor => actor.Group)
            .SingleOrDefaultAsync(
                identity => identity.Did == did.Value,
                cancellationToken);
}
