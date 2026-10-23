using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Reads eligible actors for the original human on its selected Runtime tenant owner.</summary>
public interface IAiActorContextRepository
{
    Task<IReadOnlyList<Actor>> GetAvailableActorsAsync(CancellationToken cancellationToken = default);
}
