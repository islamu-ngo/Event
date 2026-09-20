using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;

namespace Explore.Application.Features.Events.Handlers.Commands;

public sealed class PublishEventCommandHandler(EventPublicationExecutor executor)
    : ICommandHandler<PublishEventCommand, BaseCommandResponse<Guid>>
{
    public const string EventPublishedNotificationFanoutRequestedEventType =
        EventPublishedOutboxMessageFactory.EventPublishedNotificationFanoutRequestedEventType;

    public Task<BaseCommandResponse<Guid>> ExecuteAsync(
        PublishEventCommand request,
        CancellationToken cancellationToken) =>
        executor.ExecuteAsync(
            request.Id,
            request.Request,
            EventPublicationMode.Ordinary,
            cancellationToken);
}
