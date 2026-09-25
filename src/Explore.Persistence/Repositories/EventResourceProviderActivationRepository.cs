using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class EventResourceProviderActivationRepository(ExploreDbContext context)
    : IEventResourceProviderActivationRepository
{
    public Task<EventResourceProviderActivation?> GetAsync(Guid deploymentId, CancellationToken cancellationToken) =>
        context.EventResourceProviderActivations.AsNoTracking()
            .SingleOrDefaultAsync(activation => activation.Id == deploymentId, cancellationToken);

    public Task<EventResourceProviderActivation?> GetForUpdateAsync(Guid deploymentId, CancellationToken cancellationToken) =>
        context.EventResourceProviderActivations
            .SingleOrDefaultAsync(activation => activation.Id == deploymentId, cancellationToken);

    public async Task AddAsync(EventResourceProviderActivation activation, CancellationToken cancellationToken) =>
        await context.EventResourceProviderActivations.AddAsync(activation, cancellationToken);
}
