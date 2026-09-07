using Explore.Application.Features.Events.Moderation;

namespace Explore.Application.Contracts.Persistence;

public interface IEventHeavyRedactionRepository
{
    Task<EventHeavyRedactionGraph?> GetForUpdateAsync(Guid eventId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
