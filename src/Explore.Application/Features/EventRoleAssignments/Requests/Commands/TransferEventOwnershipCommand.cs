using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventRoleAssignments.Requests.Commands;

public sealed record TransferEventOwnershipCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid TenantId { get; init; }
    public Guid EventId { get; init; }
    public Guid CurrentOwnerAssignmentId { get; init; }
    public Guid NewOwnerUserId { get; init; }
    public Guid ActorUserId { get; init; }
    public DateTime StartsAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? ExpiresAtUtc { get; init; }
}
