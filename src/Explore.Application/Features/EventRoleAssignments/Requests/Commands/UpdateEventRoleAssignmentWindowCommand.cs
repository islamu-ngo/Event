using Explore.Application.Authorization;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using MediatR;

namespace Explore.Application.Features.EventRoleAssignments.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageTeam)]
public sealed record UpdateEventRoleAssignmentWindowCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid TenantId { get; init; }
    public Guid EventId { get; init; }
    public Guid AssignmentId { get; init; }
    public Guid ActorUserId { get; init; }
    public DateTime StartsAtUtc { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }

    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        EventId == Guid.Empty
        ? null
        : new EventScopedAuthorizationFacts(TenantId, EventId);
}
