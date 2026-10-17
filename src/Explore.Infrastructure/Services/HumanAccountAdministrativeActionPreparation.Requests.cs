using System.Collections.Immutable;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.GroupMembers.Requests.Commands;
using Explore.Application.Features.OrganizationMembers.Requests.Commands;
using Explore.Application.Features.OrganizationMembers.Requests.Queries;
using Explore.Application.Features.TenantUserRoleGrants.Requests.Commands;
using Explore.Application.Features.TenantUserRoleGrants.Requests.Queries;
using Explore.Domain.Enums;

namespace Explore.Infrastructure.Services;

public sealed partial class HumanAccountAdministrativeActionPreparation
{
    private static AccountMembershipTarget Target(object operation) => operation switch
    {
        RevokeTenantUserRoleGrantCommand request => new(AccountMembershipTargetKind.TenantGrant, request.Id),
        GetTenantUserRoleGrantDetailsRequest request => new(AccountMembershipTargetKind.TenantGrant, request.Id),
        UpdateOrganizationMemberRoleCommand request => new(AccountMembershipTargetKind.OrganizationMember, request.UpdateOrganizationMemberRoleDto.Id),
        DeleteOrganizationMemberCommand request => new(AccountMembershipTargetKind.OrganizationMember, request.MemberId),
        GetOrganizationMemberDetailsRequest request => new(AccountMembershipTargetKind.OrganizationMember, request.Id),
        UpdateGroupMemberRoleCommand request => new(AccountMembershipTargetKind.GroupMember, request.UpdateGroupMemberRoleDto.Id),
        DeleteGroupMemberCommand request => new(AccountMembershipTargetKind.GroupMember, request.MemberId),
        _ => new(AccountMembershipTargetKind.None, Guid.Empty)
    };

    private static AccountMembershipTargetFacts? ExpectedTarget(object operation,
        AccountMembershipTargetFacts? target, Prepared prepared) => target is null ? null : operation switch
        {
            RevokeTenantUserRoleGrantCommand => target with { RevokedAt = prepared.ChangedAt },
            UpdateOrganizationMemberRoleCommand request => target with { RoleId = (int)request.UpdateOrganizationMemberRoleDto.Role },
            UpdateGroupMemberRoleCommand request => target with { RoleId = (int)request.UpdateGroupMemberRoleDto.Role },
            DeleteOrganizationMemberCommand or DeleteGroupMemberCommand => target with { IsDeleted = true },
            _ => target
        };

    public Task<IPreparedHumanAccountAdministrativeAction> PrepareAsync(CreateTenantUserRoleGrantCommand request, CancellationToken cancellationToken) =>
        Prepare(request, null, ResourceKinds.TenantUserRoleGrant, AuthorizationActions.Create, true, cancellationToken);
    public Task<IPreparedHumanAccountAdministrativeAction> PrepareAsync(RevokeTenantUserRoleGrantCommand request, CancellationToken cancellationToken) =>
        Prepare(request, null, ResourceKinds.TenantUserRoleGrant, AuthorizationActions.Delete, true, cancellationToken);
    public Task<IPreparedHumanAccountAdministrativeAction> PrepareAsync(AddOrganizationMemberCommand request, CancellationToken cancellationToken) =>
        Prepare(request, request.RequesterUserId ?? string.Empty, ResourceKinds.OrganizationMember, AuthorizationActions.Create, true, cancellationToken);
    public Task<IPreparedHumanAccountAdministrativeAction> PrepareAsync(UpdateOrganizationMemberRoleCommand request, CancellationToken cancellationToken) =>
        Prepare(request, request.RequesterUserId ?? string.Empty, ResourceKinds.OrganizationMember, AuthorizationActions.Update, true, cancellationToken);
    public Task<IPreparedHumanAccountAdministrativeAction> PrepareAsync(DeleteOrganizationMemberCommand request, CancellationToken cancellationToken) =>
        Prepare(request, request.RequesterUserId ?? string.Empty, ResourceKinds.OrganizationMember, AuthorizationActions.Delete, true, cancellationToken);
    public Task<IPreparedHumanAccountAdministrativeAction> PrepareAsync(AddGroupMemberCommand request, CancellationToken cancellationToken) =>
        Prepare(request, request.RequesterUserId ?? string.Empty, ResourceKinds.GroupMember, AuthorizationActions.Create, false, cancellationToken);
    public Task<IPreparedHumanAccountAdministrativeAction> PrepareAsync(UpdateGroupMemberRoleCommand request, CancellationToken cancellationToken) =>
        Prepare(request, request.RequesterUserId ?? string.Empty, ResourceKinds.GroupMember, AuthorizationActions.Update, false, cancellationToken);
    public Task<IPreparedHumanAccountAdministrativeAction> PrepareAsync(DeleteGroupMemberCommand request, CancellationToken cancellationToken) =>
        Prepare(request, request.RequesterUserId ?? string.Empty, ResourceKinds.GroupMember, AuthorizationActions.Delete, false, cancellationToken);
    public Task<IPreparedHumanAccountAdministrativeAction> PrepareAsync(GetTenantUserRoleGrantDetailsRequest request, CancellationToken cancellationToken) =>
        Prepare(request, null, ResourceKinds.TenantUserRoleGrant, AuthorizationActions.View, true, cancellationToken);
    public Task<IPreparedHumanAccountAdministrativeAction> PrepareAsync(GetTenantUserRoleGrantListRequest request, CancellationToken cancellationToken) =>
        Prepare(request, null, ResourceKinds.TenantUserRoleGrant, AuthorizationActions.View, true, cancellationToken);
    public Task<IPreparedHumanAccountAdministrativeAction> PrepareAsync(GetOrganizationMemberDetailsRequest request, CancellationToken cancellationToken) =>
        Prepare(request, null, ResourceKinds.OrganizationMember, AuthorizationActions.View, true, cancellationToken);
    public Task<IPreparedHumanAccountAdministrativeAction> PrepareAsync(GetOrganizationMembersRequest request, CancellationToken cancellationToken) =>
        Prepare(request, null, ResourceKinds.OrganizationMember, AuthorizationActions.View, true, cancellationToken);

    private static void RequireTarget(object operation, Guid id)
    {
        Guid expected = operation switch
        {
            CreateTenantUserRoleGrantCommand or AddOrganizationMemberCommand or AddGroupMemberCommand => id,
            RevokeTenantUserRoleGrantCommand revoke => revoke.Id,
            UpdateOrganizationMemberRoleCommand update => update.UpdateOrganizationMemberRoleDto.Id,
            DeleteOrganizationMemberCommand delete => delete.MemberId,
            UpdateGroupMemberRoleCommand update => update.UpdateGroupMemberRoleDto.Id,
            DeleteGroupMemberCommand delete => delete.MemberId,
            _ => throw new InvalidOperationException("An account query cannot complete a mutation.")
        };
        if (id != expected)
            throw new UnauthorizedAccessException("The completed account target differs from the original request.");
    }

    private static HumanPrincipalSourceFacts Expected(object operation, HumanPrincipalSourceFacts original, Prepared prepared)
    {
        // Soft deletion retains the membership row in the P vocabulary. Revocation
        // likewise retains the exact grant; only its original prepared time changes.
        return operation switch
        {
            RevokeTenantUserRoleGrantCommand revoke => original with
            {
                TenantGrants = original.TenantGrants.Select(row => row.Id == revoke.Id
                    ? row with { RevokedAt = prepared.ChangedAt } : row).ToImmutableArray()
            },
            CreateTenantUserRoleGrantCommand create when original.TenantMemberships.Any(row =>
                row.Id == create.TenantUserRoleGrantDto.TenantUserId && row.TenantId == create.TenantId) => original with
                {
                    TenantGrants = original.TenantGrants.Add(new(prepared.TargetId, create.TenantId,
                    create.TenantUserRoleGrantDto.TenantUserId, create.TenantUserRoleGrantDto.RoleId,
                    (int)RoleScopeEnum.Tenant, null))
                },
            UpdateOrganizationMemberRoleCommand update => original with
            {
                Organizations = original.Organizations.Select(row => row.Id == update.UpdateOrganizationMemberRoleDto.Id
                    ? row with { RoleId = (int)update.UpdateOrganizationMemberRoleDto.Role } : row).ToImmutableArray()
            },
            DeleteOrganizationMemberCommand delete => original with
            {
                Organizations = original.Organizations.Select(row => row.Id == delete.MemberId
                    ? row with { IsDeleted = true } : row).ToImmutableArray()
            },
            UpdateGroupMemberRoleCommand update => original with
            {
                Groups = original.Groups.Select(row => row.Id == update.UpdateGroupMemberRoleDto.Id
                    ? row with { RoleId = (int)update.UpdateGroupMemberRoleDto.Role } : row).ToImmutableArray()
            },
            DeleteGroupMemberCommand delete => original with
            {
                Groups = original.Groups.Select(row => row.Id == delete.MemberId
                    ? row with { IsDeleted = true } : row).ToImmutableArray()
            },
            // Creation of one's own organization/group membership cannot succeed:
            // the existing body requires that membership and rejects duplicate targets.
            _ => original
        };
    }
}
