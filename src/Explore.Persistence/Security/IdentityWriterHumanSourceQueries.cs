using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.Identity;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Fixed detached human-source projections for the originally selected IdentityWriter pair.
/// The caller retains source-gate ownership and the original transaction through settlement.
/// </summary>
internal static class IdentityWriterHumanSourceQueries
{
    private static readonly JsonSerializerOptions Json = new() { AllowDuplicateProperties = false };

    internal static async Task<HumanPrincipalSourceFacts> ReadPrincipalAsync(
        LocalIdentityExecutionLease execution, HumanCommitSubject subject,
        LocalSessionAuthority? local, CancellationToken cancellationToken = default)
    {
        var owner = new OriginalRead(execution, local is not null);
        ExploreDbContext context = owner.Application;
        Guid actor = subject.PrincipalId;
        Guid? tenant = context.TenantFilterTenantId;
        if (actor == Guid.Empty || local is not null && (local.LocalSubjectId != actor
            || subject.AccountKey is not { ProviderKind: AuthenticationProviderKind.Local } localAccount
            || localAccount.Value != actor.ToString("D")))
            throw new UnauthorizedAccessException("The original principal and Local account binding are required.");

        var users = await owner.Rows(Source<User>(context).Where(row => row.Id == actor)
            .Select(row => new HumanPrincipalAccountFact(row.Id, row.IsDeleted)), cancellationToken);
        int? provider = subject.AccountKey is { } key ? (int)key.ProviderKind : null;
        string? accountKey = subject.AccountKey?.Value;
        var bindings = await owner.Rows(Source<UserExternalLogin>(context)
            .Where(row => row.UserId == actor && row.AuthenticationProviderId == provider && row.ProviderKey == accountKey)
            .Select(row => new HumanPrincipalBindingFact(row.Id, row.UserId, row.AuthenticationProviderId, row.ProviderKey)),
            cancellationToken);
        var platformQuery = Source<PlatformUserRole>(context).Where(row => row.UserId == actor);
        var memberQuery = Source<TenantUser>(context).Where(row => row.UserId == actor);
        var grantQuery = from grant in Source<TenantUserRoleGrant>(context)
                         join member in memberQuery
                             on new { Id = grant.TenantUserId, grant.TenantId } equals new { member.Id, member.TenantId }
                         select grant;
        var organizationMembers = Source<OrganizationMember>(context).Where(row => row.UserId == actor);
        var groupMembers = Source<GroupMember>(context).Where(row => row.UserId == actor);
        var roleIds = platformQuery.Select(row => row.RoleId)
            .Union(grantQuery.Select(row => row.RoleId))
            .Union(organizationMembers.Select(row => row.RoleId))
            .Union(groupMembers.Select(row => row.RoleId));
        var platform = await owner.Rows(platformQuery.Select(row => new HumanPrincipalPlatformGrantFact(row.Id, row.RoleId)),
            cancellationToken);
        var roles = await owner.Rows(Source<Role>(context).Where(row => roleIds.Contains(row.Id))
            .Select(row => new HumanPrincipalRoleFact(row.Id, row.RoleScopeId, row.MasterCode,
                row.MasterCode == "platform.admin")), cancellationToken);
        var members = await owner.Rows(memberQuery.Select(row =>
            new HumanPrincipalTenantMembershipFact(row.Id, row.TenantId, row.StatusId, row.IsDeleted)), cancellationToken);
        var grants = await owner.Rows(grantQuery.Select(row => new HumanPrincipalTenantGrantFact(
            row.Id, row.TenantId, row.TenantUserId, row.RoleId, row.RoleScopeId, row.RevokedAt)), cancellationToken);
        var tenants = await owner.Rows(
            from row in Source<Tenant>(context)
            join status in Source<TenantStatus>(context) on row.TenantStatusId equals status.Id
            where row.Id == tenant || memberQuery.Select(member => member.TenantId).Contains(row.Id)
            select new HumanPrincipalTenantFact(row.Id, row.TenantStatusId, status.IsActiveState), cancellationToken);
        var organizations = await owner.Rows(
            from member in organizationMembers
            join placement in Source<OrganizationTenant>(context)
                on new { Id = member.OrganizationTenantId, member.TenantId } equals new { placement.Id, placement.TenantId }
            join organization in Source<Organization>(context) on placement.OrganizationId equals organization.Id
            select new HumanPrincipalOwnerMembershipFact(member.Id, member.TenantId, placement.Id, placement.OrganizationId,
                member.RoleId, member.IsDeleted, placement.IsDeleted, organization.IsDeleted, placement.ApprovalStatusId),
            cancellationToken);
        var groups = await owner.Rows(
            from member in groupMembers
            join placement in Source<GroupTenant>(context)
                on new { Id = member.GroupTenantId, member.TenantId } equals new { placement.Id, placement.TenantId }
            join ownerGroup in Source<Group>(context) on placement.GroupId equals ownerGroup.Id
            select new HumanPrincipalOwnerMembershipFact(member.Id, member.TenantId, placement.Id, placement.GroupId,
                member.RoleId, member.IsDeleted, placement.IsDeleted, ownerGroup.IsDeleted, placement.ApprovalStatusId),
            cancellationToken);
        var mappedPermissions = from mapping in Source<RolePermission>(context)
                                join permission in Source<Permission>(context) on mapping.PermissionId equals permission.Id
                                select new { mapping.RoleId, mapping.PermissionId, permission.MasterCode, permission.IsActive };
        var permissions = await owner.Rows(mappedPermissions.Where(row => roleIds.Contains(row.RoleId))
            .Select(row => new HumanPrincipalPermissionFact(row.RoleId, row.PermissionId, row.MasterCode, row.IsActive,
                row.MasterCode == PermissionCodes.EventCreate, row.MasterCode == PermissionCodes.EventManageFinance)),
            cancellationToken);
        owner.RequireCurrent();
        bool mappingsExist = await Source<RolePermission>(context).AnyAsync(cancellationToken);
        owner.RequireCurrent();

        // These lists intentionally retain multiplicity and omit an owner/deletion/approval join.
        var organizationList = from member in organizationMembers
                               join placement in Source<OrganizationTenant>(context)
                                   on new { Id = member.OrganizationTenantId, member.TenantId }
                                   equals new { placement.Id, placement.TenantId }
                               where member.TenantId == tenant && placement.TenantId == tenant
                                   && !member.IsDeleted && !placement.IsDeleted
                               select new { member.RoleId, OwnerId = placement.OrganizationId };
        var groupList = from member in groupMembers
                        join placement in Source<GroupTenant>(context)
                            on new { Id = member.GroupTenantId, member.TenantId } equals new { placement.Id, placement.TenantId }
                        where member.TenantId == tenant && placement.TenantId == tenant
                            && !member.IsDeleted && !placement.IsDeleted
                        select new { member.RoleId, OwnerId = placement.GroupId };
        var createOrganizations = await owner.Rows(organizationList.Where(row => mappingsExist
            ? mappedPermissions.Any(permission => permission.RoleId == row.RoleId && permission.IsActive
                && permission.MasterCode == PermissionCodes.EventCreate)
            : row.RoleId == (int)RoleEnum.OrgAdmin).Select(row => row.OwnerId), cancellationToken);
        var createGroups = await owner.Rows(groupList.Where(row => mappingsExist
            ? mappedPermissions.Any(permission => permission.RoleId == row.RoleId && permission.IsActive
                && permission.MasterCode == PermissionCodes.EventCreate)
            : row.RoleId == (int)RoleEnum.GroupAdmin).Select(row => row.OwnerId), cancellationToken);
        var financeOrganizations = await owner.Rows(organizationList.Where(row => mappingsExist
            ? mappedPermissions.Any(permission => permission.RoleId == row.RoleId && permission.IsActive
                && permission.MasterCode == PermissionCodes.EventManageFinance)
            : row.RoleId == (int)RoleEnum.OrgAdmin).Select(row => row.OwnerId), cancellationToken);
        var financeGroups = await owner.Rows(groupList.Where(row => mappingsExist
            ? mappedPermissions.Any(permission => permission.RoleId == row.RoleId && permission.IsActive
                && permission.MasterCode == PermissionCodes.EventManageFinance)
            : row.RoleId == (int)RoleEnum.GroupAdmin).Select(row => row.OwnerId), cancellationToken);

        HumanPrincipalLocalSessionFacts? localFacts = null;
        if (local is not null)
        {
            var privateAccounts = await owner.Rows(owner.Identity.Set<LocalIdentityUser>().AsNoTracking()
                .Where(row => row.Id == actor).Select(row => new
                {
                    row.Id,
                    row.PasswordHash,
                    row.SecurityStamp,
                    row.EmailConfirmed
                }), cancellationToken);
            var accounts = privateAccounts.Select(row => new HumanPrincipalLocalAccountFact(row.Id,
                !string.IsNullOrWhiteSpace(row.PasswordHash),
                string.Equals(row.SecurityStamp, local.SecurityStamp, StringComparison.Ordinal),
                row.EmailConfirmed)).ToImmutableArray();
            var tokens = await owner.Rows(owner.Identity.Set<IdentityUserToken<Guid>>().AsNoTracking()
                .Where(row => row.UserId == actor && row.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                    && row.Name == LocalCredentialStateMetadata.TokenName).Select(row => row.Value), cancellationToken);
            string? metadata = tokens.SingleOrDefault();
            LocalCredentialStateMetadata? state;
            try { state = string.IsNullOrWhiteSpace(metadata) ? null : JsonSerializer.Deserialize<LocalCredentialStateMetadata>(metadata, Json); }
            catch (JsonException) { state = null; }
            catch (ArgumentException) { state = null; }
            var operations = await owner.Rows(owner.Identity.Set<LocalIdentityCredentialOperation>().AsNoTracking()
                .Where(row => row.LocalSubjectId == actor).Select(row => new HumanPrincipalLocalOperationFact(
                    row.Id, row.LocalSubjectId, (int)row.Kind, (int)row.Stage, row.PersonalActorId, row.ExternalLoginId)),
                cancellationToken);
            var actors = await owner.Rows(Source<Actor>(context).Where(row => row.UserId == actor)
                .Select(row => new HumanPrincipalPersonalActorFact(row.Id, row.UserId, row.ActorTypeId,
                    row.IsDeleted, row.IsSuspended)), cancellationToken);
            string? delivery = null;
            if (accounts is [var current] && !current.EmailConfirmed)
                delivery = (await owner.Rows(context.SystemSettings.AsNoTracking()
                    .Where(row => row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled)
                    .Select(row => row.Value), cancellationToken)).SingleOrDefault();
            localFacts = new(accounts, state, operations, actors,
                delivery is null ? null : JsonSerializer.Deserialize<bool>(delivery));
        }
        owner.RequireCurrent();
        return new(actor, tenant, users, bindings, platform, roles, members, grants, tenants, organizations, groups,
            permissions, mappingsExist, createOrganizations, createGroups, financeOrganizations, financeGroups, localFacts);
    }

    internal static async Task<HumanAuthorizationRouteFacts> ReadRouteAsync(
        LocalIdentityExecutionLease execution, HumanCommitSubject subject,
        CancellationToken cancellationToken = default)
    {
        var owner = new OriginalRead(execution, includeIdentity: false);
        if (subject.PrincipalId == Guid.Empty)
            throw new UnauthorizedAccessException("The original principal is required.");
        string[] keys = HumanAuthorizationRouteFacts.Keys.ToArray();
        var system = await owner.Rows(owner.Application.SystemSettings.AsNoTracking()
            .Where(row => keys.Contains(row.SettingKey))
            .Select(row => new
            {
                row.SettingKey,
                row.Value,
                row.SettingValueTypeId,
                row.IsLocked,
                row.Description,
                row.Category,
                row.AllowedValues
            }), cancellationToken);
        var systemByKey = system.ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        Guid? tenant = owner.Application.TenantFilterTenantId;
        var tenantByKey = new Dictionary<string, TenantSettingFacts>(StringComparer.Ordinal);
        if (tenant is { } selected)
        {
            var overrides = await owner.Rows(owner.Application.Set<TenantSetting>().AsNoTracking()
                .Where(row => row.TenantId == selected && keys.Contains(row.SettingKey))
                .Select(row => new { row.SettingKey, row.Value, row.IsLocked }), cancellationToken);
            tenantByKey = overrides.ToDictionary(row => row.SettingKey,
                row => new TenantSettingFacts(row.Value, row.IsLocked), StringComparer.Ordinal);
        }
        owner.RequireCurrent();
        return new(subject.PrincipalId, tenant, keys.Select(key =>
        {
            systemByKey.TryGetValue(key, out var instance);
            tenantByKey.TryGetValue(key, out var scoped);
            return new HumanAuthorizationRouteSettingFact(key, instance is null ? null :
                new SystemSettingFacts(instance.Value, (SettingValueType)instance.SettingValueTypeId, instance.IsLocked,
                    instance.Description, instance.Category, instance.AllowedValues), scoped);
        }).ToImmutableArray());
    }

    private static IQueryable<T> Source<T>(ExploreDbContext context) where T : class =>
        // Fixed principal facts must include deleted candidates and the exact subject's cross-tenant rows.
        // Every caller adds its declared subject/join predicate; this never changes the context's tenant lease.
        context.Set<T>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete]);

    private sealed class OriginalRead
    {
        private readonly LocalIdentityExecutionLease _execution;
        private readonly IDbContextTransaction? _applicationTransaction;
        private readonly IDbContextTransaction? _identityTransaction;
        private readonly bool _includeIdentity;
        internal ExploreDbContext Application { get; }
        internal DbContext Identity { get; }

        internal OriginalRead(LocalIdentityExecutionLease execution, bool includeIdentity)
        {
            _execution = execution;
            Application = execution.Application ?? throw new UnauthorizedAccessException("The selected application owner is required.");
            Identity = execution.Identity ?? throw new UnauthorizedAccessException("The selected identity owner is required.");
            _applicationTransaction = Application.Database.CurrentTransaction;
            _identityTransaction = Identity.Database.CurrentTransaction;
            _includeIdentity = includeIdentity;
            RequireCurrent();
        }

        internal void RequireCurrent()
        {
            _execution.RequireCurrent(Application, Identity);
            HumanSecuritySourceGate.RequireCurrent(Application);
            if (_includeIdentity)
                LocalIdentityLifecycleGateCustody.RequireHeld(Identity);
            if (!ReferenceEquals(Application.Database.CurrentTransaction, _applicationTransaction)
                || !ReferenceEquals(Identity.Database.CurrentTransaction, _identityTransaction))
                throw new UnauthorizedAccessException("The original human source transaction changed.");
        }

        internal async Task<ImmutableArray<T>> Rows<T>(IQueryable<T> query, CancellationToken cancellationToken)
        {
            RequireCurrent();
            T[] rows = await query.ToArrayAsync(cancellationToken);
            RequireCurrent();
            return rows.ToImmutableArray();
        }
    }
}
