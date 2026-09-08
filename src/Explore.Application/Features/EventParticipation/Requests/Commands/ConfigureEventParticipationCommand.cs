using Explore.Application.Authorization;
using Explore.Application.DTOs.Event;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventParticipation.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrations)]
public sealed record ConfigureEventParticipationCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventId { get; init; }
    public Guid ExpectedConcurrencyStamp { get; init; }
    public required ConfigureEventParticipationDto ParticipationConfiguration { get; init; }

    string? ISecureRequest.ResourceId => EventId.ToString();
}
