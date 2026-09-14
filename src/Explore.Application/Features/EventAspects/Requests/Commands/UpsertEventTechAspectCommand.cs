namespace Explore.Application.Features.EventAspects.Requests.Commands;

using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.EventAspects;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Update)]
public sealed record CreateEventTechAspectCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventId { get; init; }
    public required CreateUpdateTechAspectDto AspectDto { get; init; }
    string? ISecureRequest.ResourceId => EventId.ToString();
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Update)]
public sealed record UpdateEventTechAspectCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventId { get; init; }
    public required UpdateEventTechAspectDto AspectDto { get; init; }
    string? ISecureRequest.ResourceId => EventId.ToString();
}
