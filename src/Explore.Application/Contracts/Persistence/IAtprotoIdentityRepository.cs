using Explore.Domain;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Contracts.Persistence;

public interface IAtprotoIdentityRepository : IGenericRepository<AtprotoIdentity, Guid>
{
    Task<AtprotoIdentity?> GetByDid(
        AtprotoDid did,
        CancellationToken cancellationToken = default);
}
