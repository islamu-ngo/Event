using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSession;

namespace Explore.Application.Features.EventSessions.Requests.Queries;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement)]
public sealed record GetManagedEventSessionDetailsRequest : IQuery<EventSessionDto?>, ISecureRequest
{
    public Guid EventId { get; init; }
    public Guid Id { get; init; }

    string? ISecureRequest.ResourceId => EventId.ToString();
}
