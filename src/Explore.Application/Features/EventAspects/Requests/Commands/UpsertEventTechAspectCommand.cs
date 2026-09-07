namespace Explore.Application.Features.EventAspects.Requests.Commands;

using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.EventAspects;
using Explore.Application.Responses;
using MediatR;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Update)]
public sealed record CreateEventTechAspectCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventId { get; init; }
    public required CreateUpdateTechAspectDto AspectDto { get; init; }
    string? ISecureRequest.ResourceId => EventId.ToString();
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Update)]
public sealed record UpdateEventTechAspectCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventId { get; init; }
    public required UpdateEventTechAspectDto AspectDto { get; init; }
    string? ISecureRequest.ResourceId => EventId.ToString();
}
