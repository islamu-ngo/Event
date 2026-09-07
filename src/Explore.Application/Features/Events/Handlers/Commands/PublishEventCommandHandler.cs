using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using MediatR;

namespace Explore.Application.Features.Events.Handlers.Commands;

public sealed class PublishEventCommandHandler(EventPublicationExecutor executor)
    : IRequestHandler<PublishEventCommand, BaseCommandResponse<Guid>>
{
    public const string EventPublishedNotificationFanoutRequestedEventType =
        EventPublishedOutboxMessageFactory.EventPublishedNotificationFanoutRequestedEventType;

    public Task<BaseCommandResponse<Guid>> Handle(
        PublishEventCommand request,
        CancellationToken cancellationToken) =>
        executor.ExecuteAsync(
            request.Id,
            request.Request,
            EventPublicationMode.Ordinary,
            cancellationToken);
}
