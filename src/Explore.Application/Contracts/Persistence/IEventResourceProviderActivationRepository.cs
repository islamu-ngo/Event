using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventResourceProviderActivationRepository
{
    Task<EventResourceProviderActivation?> GetAsync(Guid deploymentId, CancellationToken cancellationToken);
    Task<EventResourceProviderActivation?> GetForUpdateAsync(Guid deploymentId, CancellationToken cancellationToken);
    Task AddAsync(EventResourceProviderActivation activation, CancellationToken cancellationToken);
}
