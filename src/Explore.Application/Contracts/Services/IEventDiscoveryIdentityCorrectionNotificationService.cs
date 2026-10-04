using Explore.Application.Features.Events.Discovery.Commands;

namespace Explore.Application.Contracts.Services;

public interface IEventDiscoveryIdentityCorrectionNotificationService
{
    Task DeliverAsync(
        Guid outboxMessageId,
        EventDiscoveryIdentityCorrectionRequested request,
        CancellationToken cancellationToken = default);
}
