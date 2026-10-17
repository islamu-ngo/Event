using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventRoleAssignment;

namespace Explore.Application.Features.EventRoleAssignments.Requests.Queries;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageTeam)]
public sealed record GetManagedEventTeamRequest(Guid EventId, bool IncludeInactive)
    : IQuery<ManagedEventTeamDto>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId.ToString("D");
}
