using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Event;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Events.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Update)]
public sealed record UpdateEventCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventId { get; init; }

    public Guid ExpectedConcurrencyStamp { get; init; }

    public required UpdateEventDto UpdateEventDto { get; init; }

    string? ISecureRequest.ResourceId => EventId.ToString();
}
