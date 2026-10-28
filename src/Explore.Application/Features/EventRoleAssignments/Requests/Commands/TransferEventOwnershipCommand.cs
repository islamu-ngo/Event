using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventRoleAssignments.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.TransferOwnership)]
public sealed record TransferEventOwnershipCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid TenantId { get; init; }
    public Guid EventId { get; init; }
    public Guid CurrentOwnerAssignmentId { get; init; }
    public Guid NewOwnerUserId { get; init; }
    public DateTime StartsAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? ExpiresAtUtc { get; init; }

    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => EventId == Guid.Empty
        ? null : new EventScopedAuthorizationFacts(TenantId, EventId);
}
