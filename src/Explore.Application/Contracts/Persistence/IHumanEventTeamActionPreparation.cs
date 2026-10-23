using System.Collections.Immutable;
using Explore.Application.Authorization;
using Explore.Application.Features.EventRoleAssignments.Requests.Commands;
using Explore.Application.Features.EventRoleAssignments.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Contracts.Services;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Closed original Event/manage-team questions, never a reusable administrator grant.</summary>
public interface IHumanEventTeamActionPreparation
{
    Task<IPreparedHumanEventTeamAction> PrepareAsync(AssignEventRoleByEmailCommand request, CancellationToken token);
    Task<IPreparedHumanEventTeamAction> PrepareAsync(AssignEventRoleCommand request, CancellationToken token);
    Task<IPreparedHumanEventTeamAction> PrepareAsync(RevokeEventRoleAssignmentCommand request, CancellationToken token);
    Task<IPreparedHumanEventTeamAction> PrepareAsync(GetEventTeamListRequest request, CancellationToken token);
    Task<IPreparedHumanEventTeamAction> PrepareAsync(GetAssignableEventRolePresetsRequest request, CancellationToken token);
    void ConsumeHandoff(AssignEventRoleCommand request, IPreparedHumanEventTeamAction prepared);
}

public interface IPreparedHumanEventTeamAction
{
    DateTime ChangedAt { get; }
    Task AcquireAsync(object originalRequest, CancellationToken token);
    void RequireTeamRead(GetEventTeamListRequest originalRequest);
    EventAuthorityForUser GetTeamAuthority();
    AssignEventRoleCommand BindResolvedTarget(AssignEventRoleByEmailCommand original, Guid userId);
    EventRoleAssignmentAuthorityResult CanAssignRole(int roleId);
    IReadOnlyList<EventRolePreset> GetAssignablePresets();
    void Complete(Guid assignmentId);
}

/// <summary>
/// The email owner invokes the actual inner body with one acquired original action.
/// It deliberately does not reenter the decorated command pipeline or start a second owner.
/// </summary>
public interface IHumanEventTeamAssignmentOwner
{
    Task<BaseCommandResponse<Guid>> ExecuteHandoffAsync(AssignEventRoleCommand request,
        IPreparedHumanEventTeamAction prepared, CancellationToken token);
}

/// <summary>Definition and target inputs that the ordinary assignment projection does not contain.</summary>
public interface IEventTeamPolicySource
{
    Task<EventTeamPolicyFacts> ObserveAsync(EventTeamSourceTarget target, CancellationToken token);
    Task<IEventTeamPolicyLease> AcquireAsync(EventTeamSourceTarget target, IPolicySourceSetLease sources, CancellationToken token);
}

public interface IEventTeamPolicyLease
{
    EventTeamPolicyFacts Facts { get; }
    void RequireOwner();
    Task<EventTeamPolicyFacts> ValidateAsync(CancellationToken token);
}

public sealed record EventTeamSourceTarget(Guid EventId, Guid? UserId = null, string? Email = null)
{
    public override string ToString() => nameof(EventTeamSourceTarget);
}

public sealed record EventTeamPolicyFacts(
    ImmutableArray<EventTeamRoleFact> Roles,
    ImmutableArray<EventTeamPermissionFact> Permissions,
    ImmutableArray<EventTeamRolePermissionFact> RolePermissions,
    ImmutableArray<EventTeamAssignmentFact> Assignments,
    Guid? ResolvedUserId);

public sealed record EventTeamRoleFact(int Id, int RoleScopeId, string MasterCode, string FullName, string? Description);
public sealed record EventTeamPermissionFact(int Id, string MasterCode, bool IsActive, bool IsFiltered);
public sealed record EventTeamRolePermissionFact(int RoleId, int PermissionId);
public sealed record EventTeamAssignmentFact(
    Guid Id, Guid TenantId, Guid EventId, Guid UserId, int RoleId, int Status, DateTime StartsAtUtc,
    DateTime? ExpiresAtUtc, DateTime? RevokedAtUtc, Guid? RevokedByUserId, long Version, Guid? CreatedBy);
