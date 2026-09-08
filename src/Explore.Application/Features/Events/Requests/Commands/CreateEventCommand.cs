using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Event;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Events.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Create)]
public sealed record CreateEventCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public const string PreCreateResourceId = "create";

    public required CreateEventDto EventDto { get; init; }

    string? ISecureRequest.ResourceId => PreCreateResourceId;

    // No event row exists yet, so the requested owning organization or group is the only context a
    // policy can weigh. The handler still verifies the caller's membership before persisting.
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => new PreCreateAuthorizationFacts(
        Guid.Empty,
        ParentEventId: null,
        OrganizationId: EventDto.OrganizationId,
        GroupId: EventDto.GroupId);
}
