using Explore.Domain.Enums;
using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.EventRoleAssignment;

public sealed record EventTeamMemberDto
{
    [JsonIgnore]
    public Guid TenantId { get; init; }

    [JsonIgnore]
    public Guid EventId { get; init; }

    public Guid AssignmentId { get; init; }
    public Guid UserId { get; init; }
    public required string UserEmail { get; init; }
    public required string UserFullName { get; init; }
    public int RoleId { get; init; }
    public required string RoleName { get; init; }
    public required string RoleMasterCode { get; init; }
    public EventRoleAssignmentStatus Status { get; init; }
    public DateTime StartsAtUtc { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public bool IsEffective { get; init; }
    public DateTime CreatedAt { get; init; }
    public Guid? CreatedBy { get; init; }
}
