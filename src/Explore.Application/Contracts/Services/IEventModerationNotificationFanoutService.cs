using Explore.Application.Models.InternalEvents;

namespace Explore.Application.Contracts.Services;

public interface IEventModerationNotificationFanoutService
{
    Task FanoutLightModerationAsync(
        EventLightModeratedNotificationFanoutRequested request,
        CancellationToken cancellationToken = default);

    Task FanoutHeavyRedactionAsync(
        EventHeavyRedactedNotificationFanoutRequested request,
        CancellationToken cancellationToken = default);
}
