using System.Collections.Immutable;
using Explore.Application.Features.GroupMembers.Requests.Commands;
using Explore.Application.Features.OrganizationMembers.Requests.Commands;
using Explore.Application.Features.OrganizationMembers.Requests.Queries;
using Explore.Application.Features.TenantUserRoleGrants.Requests.Commands;
using Explore.Application.Features.TenantUserRoleGrants.Requests.Queries;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Original human custody for the closed account-membership owners. Preparation precedes
/// the selected non-replaying owner; acquisition precedes every business read and write.
/// Unannotated Group queries are public and deliberately have no preparation overload.
/// </summary>
public interface IHumanAccountAdministrativeActionPreparation
{
    Task<IPreparedHumanAccountAdministrativeAction> PrepareAsync(CreateTenantUserRoleGrantCommand request, CancellationToken cancellationToken);
    Task<IPreparedHumanAccountAdministrativeAction> PrepareAsync(RevokeTenantUserRoleGrantCommand request, CancellationToken cancellationToken);
    Task<IPreparedHumanAccountAdministrativeAction> PrepareAsync(AddOrganizationMemberCommand request, CancellationToken cancellationToken);
    Task<IPreparedHumanAccountAdministrativeAction> PrepareAsync(UpdateOrganizationMemberRoleCommand request, CancellationToken cancellationToken);
    Task<IPreparedHumanAccountAdministrativeAction> PrepareAsync(DeleteOrganizationMemberCommand request, CancellationToken cancellationToken);
    Task<IPreparedHumanAccountAdministrativeAction> PrepareAsync(AddGroupMemberCommand request, CancellationToken cancellationToken);
    Task<IPreparedHumanAccountAdministrativeAction> PrepareAsync(UpdateGroupMemberRoleCommand request, CancellationToken cancellationToken);
    Task<IPreparedHumanAccountAdministrativeAction> PrepareAsync(DeleteGroupMemberCommand request, CancellationToken cancellationToken);
    Task<IPreparedHumanAccountAdministrativeAction> PrepareAsync(GetTenantUserRoleGrantDetailsRequest request, CancellationToken cancellationToken);
    Task<IPreparedHumanAccountAdministrativeAction> PrepareAsync(GetTenantUserRoleGrantListRequest request, CancellationToken cancellationToken);
    Task<IPreparedHumanAccountAdministrativeAction> PrepareAsync(GetOrganizationMemberDetailsRequest request, CancellationToken cancellationToken);
    Task<IPreparedHumanAccountAdministrativeAction> PrepareAsync(GetOrganizationMembersRequest request, CancellationToken cancellationToken);
}

public interface IPreparedHumanAccountAdministrativeAction
{
    Guid PrincipalId { get; }
    DateTime ChangedAt { get; }
    Task AcquireAsync(object originalRequest, CancellationToken cancellationToken);

    /// <summary>
    /// Marks the successful body, not an authorization decision. Only the transition compiled
    /// from the original request is permitted; final admission independently reads its sources.
    /// Failure responses leave original sources unchanged. Queries never call this method.
    /// </summary>
    void Complete(Guid targetId);
}

/// <summary>
/// Retains role and permission definitions even when an intentional self-demotion removes
/// their last actor reference. The ordinary P projection alone can no longer see those rows.
/// This source grants no authority and never substitutes another selected database.
/// </summary>
public interface IAccountMembershipPolicySource
{
    Task<AccountMembershipPolicyFacts> ObserveAsync(AccountMembershipTarget target, CancellationToken cancellationToken);
    Task<IAccountMembershipPolicyLease> AcquireAsync(AccountMembershipTarget target,
        IPolicySourceSetLease sourceSet, CancellationToken cancellationToken);
}

public interface IAccountMembershipPolicyLease
{
    AccountMembershipPolicyFacts Facts { get; }
    Task<AccountMembershipPolicyFacts> ValidateAsync(CancellationToken cancellationToken);
}

public sealed record AccountMembershipPolicyFacts(
    ImmutableArray<HumanPrincipalRoleFact> Roles,
    ImmutableArray<HumanPrincipalPermissionFact> Permissions,
    AccountMembershipTargetFacts? Target);

public enum AccountMembershipTargetKind
{
    None,
    TenantGrant,
    OrganizationMember,
    GroupMember
}

public sealed record AccountMembershipTarget(AccountMembershipTargetKind Kind, Guid Id);

/// <summary>
/// Persisted target identity and authority-bearing fields, including soft-deleted rows.
/// Deletion must retain the original subject/placement rather than erasing the question.
/// </summary>
public sealed record AccountMembershipTargetFacts(
    Guid Id, Guid TenantId, Guid UserId, Guid PlacementId, Guid OwnerId, int RoleId,
    int RoleScopeId, bool IsDeleted, bool PlacementIsDeleted, bool OwnerIsDeleted,
    DateTime? RevokedAt, int? PositionId);
