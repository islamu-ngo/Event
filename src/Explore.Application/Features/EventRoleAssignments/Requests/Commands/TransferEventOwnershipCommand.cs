using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventRoleAssignments.Requests.Commands;

public sealed record TransferEventOwnershipCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid TenantId { get; init; }
    public Guid EventId { get; init; }
    public Guid CurrentOwnerAssignmentId { get; init; }
    public Guid NewOwnerUserId { get; init; }
    public Guid ActorUserId { get; init; }
    public DateTime StartsAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? ExpiresAtUtc { get; init; }
}
