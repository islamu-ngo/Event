using Explore.Application.Authorization;
using Explore.Application.DTOs.Event;
using MediatR;

namespace Explore.Application.Features.Events.Requests.Queries;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement)]
public sealed record GetEventManagementDetailsRequest : IRequest<EventDto?>, ISecureRequest
{
    public Guid Id { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
