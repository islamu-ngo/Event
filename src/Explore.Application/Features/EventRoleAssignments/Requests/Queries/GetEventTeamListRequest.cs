using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventRoleAssignment;
using Explore.Domain.Constants;

namespace Explore.Application.Features.EventRoleAssignments.Requests.Queries;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageTeam)]
public sealed record GetEventTeamListRequest : IQuery<List<EventTeamMemberDto>>, ISecureRequest
{
    public Guid TenantId { get; init; }
    public Guid EventId { get; init; }
    public bool IncludeInactive { get; init; }

    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        EventId == Guid.Empty
        ? null
        : new EventScopedAuthorizationFacts(TenantId, EventId);
}
