using Explore.Application.DTOs.Event;

namespace Explore.Application.Contracts.Services;

public interface IEventDetailsProjectionService
{
    Task<EventDto?> BuildAsync(Guid eventId, CancellationToken cancellationToken);
    Task<EventDto?> BuildByPublicCodeAsync(string publicCode, CancellationToken cancellationToken);

    Task ResolveImageUrlsAsync(EventDto eventDto, CancellationToken cancellationToken);
}
