using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Retains authentic human source custody through a standalone actor-context read.</summary>
public interface IHumanActorContextReader
{
    Task<IReadOnlyList<Actor>> ReadAsync(
        Guid tenantId, Guid userId, CancellationToken cancellationToken = default);
}
