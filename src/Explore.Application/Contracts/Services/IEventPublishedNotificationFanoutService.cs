using Explore.Application.Models.InternalEvents;

namespace Explore.Application.Contracts.Services;

public interface IEventPublishedNotificationFanoutService
{
    Task FanoutAsync(EventPublishedNotificationFanoutRequested request, CancellationToken cancellationToken = default);
}
