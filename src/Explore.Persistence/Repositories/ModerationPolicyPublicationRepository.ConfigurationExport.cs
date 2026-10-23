using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using Explore.Application.Features.ConfigurationManifest.Validation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationPolicyPublicationRepository
{
    private readonly ExploreDbContextExecutionLease? instanceExportLease;
    private readonly IHttpContextAccessor? instanceExportHttp;
    private readonly IInstanceAdministratorCommitAuthority? instanceExportAuthority;
    private readonly HumanAdministratorCommitScope? instanceExportScope;
    private readonly HumanAuthenticationLifetime? instanceExportLifetime;

    public async Task<IReadOnlyList<ModerationPolicyPublication>> AcquireConfigurationExportAsync(
        ConfigurationImportTarget target, CancellationToken cancellationToken = default)
    {
        var transaction = context.Database.CurrentTransaction;
        var nativeAuthority = new ModerationQualificationAuthority(context, admin, machine);
        var requestTenant = context.TenantFilterTenantId;
        bool nativeInstanceOwner = context.IsConfigurationInstanceImportOwner;
        if (transaction is null || !CurrentTransaction(transaction.TransactionId)
            || machine.IsMachineCaller || target is null
            || target.Scope is not (ConfigurationImportScope.Instance or ConfigurationImportScope.Tenant)
            || target.Scope == ConfigurationImportScope.Tenant && target.TenantId != requestTenant
            || nativeInstanceOwner && (target.Scope != ConfigurationImportScope.Instance || requestTenant is not null)
            || !nativeInstanceOwner && (requestTenant is not { } selectedTenant
                || !await nativeAuthority.ProviderTenantMatchesAsync(selectedTenant, cancellationToken)))
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        if (context.ChangeTracker.HasChanges())
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);

        // Definition export remains usable before moderation installation. Its read authority is
        // therefore acquired from native human/role rows, not a required installation epoch.
        if (nativeInstanceOwner)
            await RequireNativeInstanceExportAuthorityAsync(cancellationToken);
        else
            await AcquireExportPrincipalAsync(target.Scope, requestTenant!.Value, cancellationToken);
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (host is null)
        {
            if (!CurrentTransaction(transaction.TransactionId)
                || context.TenantFilterTenantId != requestTenant
                || !nativeInstanceOwner && !await nativeAuthority.ProviderTenantMatchesAsync(requestTenant!.Value, cancellationToken))
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
            if (nativeInstanceOwner)
                await RequireNativeInstanceExportAuthorityAsync(cancellationToken);
            return Array.Empty<ModerationPolicyPublication>();
        }
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, host.Id, cancellationToken);
        var fencedHost = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (fencedHost is null || fencedHost.Id != host.Id || fencedHost.TrustDomainId != host.TrustDomainId
            || fencedHost.SolutionId != host.SolutionId || fencedHost.InstanceId != host.InstanceId
            || fencedHost.MaintenanceServicePrincipalId != host.MaintenanceServicePrincipalId)
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
        await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(
            context, host.MaintenanceServicePrincipalId, cancellationToken);
        if (!await context.ServicePrincipals.AsNoTracking().AnyAsync(
                row => row.Id == host.MaintenanceServicePrincipalId && !row.IsDeleted, cancellationToken))
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);

        var instance = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId);
        var tenantQuery = context.Tenants.AsNoTracking();
        if (target.Scope == ConfigurationImportScope.Instance)
            tenantQuery = tenantQuery.IgnoreTenantFilter(TenantFilterBypassReasons.InstanceConfigurationManifestExport);
        else
            tenantQuery = tenantQuery.Where(row => row.Id == requestTenant);
        var tenants = await tenantQuery.Where(row => row.TenantStatus.IsActiveState).OrderBy(row => row.Id)
            .Take(ConfigurationManifestValidator.MaximumTenantCount + 1).ToArrayAsync(cancellationToken);
        if (tenants.Length > ConfigurationManifestValidator.MaximumTenantCount)
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
        foreach (var tenant in tenants)
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, tenant.Id, cancellationToken);
            await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(
                context, [tenant.TenantStatusId], cancellationToken);
            if (!await tenantQuery.AnyAsync(row => row.Id == tenant.Id
                    && row.TenantStatusId == tenant.TenantStatusId && row.TenantStatus.IsActiveState, cancellationToken))
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
        }

        var scopes = tenants.Select(tenant => AuthorityScope.Tenant(instance, tenant.Id)).ToList();
        if (target.Scope == ConfigurationImportScope.Instance)
            scopes.Insert(0, instance);
        var result = new List<ModerationPolicyPublication>();
        var priorTrustDomain = context.ModerationTrustDomainId;
        try
        {
            context.ModerationTrustDomainId = host.TrustDomainId;
            // Preserve writer ordering: native source epochs precede jurisdiction slots. Discovery
            // yields only fence coordinates; selected ownership is checked again below.
            var discovery = context.ModerationPolicyPublications.AsNoTracking();
            if (target.Scope == ConfigurationImportScope.Instance)
                discovery = discovery.IgnoreQueryFilters(["ModerationAuthority"]);
            var scopeIds = scopes.Select(scope => scope.Id).ToArray();
            var sources = await discovery.Where(row => row.HostBindingId == host.Id
                    && row.TrustDomainId == host.TrustDomainId
                    && row.SolutionId == host.SolutionId && row.InstanceId == host.InstanceId
                    && (row.JurisdictionTenantId == null && scopeIds.Contains(row.InstanceId)
                        || row.JurisdictionTenantId != null && scopeIds.Contains(row.JurisdictionTenantId.Value)))
                .Select(row => row.AuthoritySourceId).Distinct().Order().ToArrayAsync(cancellationToken);
            foreach (var source in sources)
                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(context, source, cancellationToken);
            foreach (var scope in scopes)
            {
                await using var slot = await AcquireSlotAsync(host.Id, scope, cancellationToken);
                // Only definition metadata crosses tenant scope, after native instance authority.
                // This replaces the exact named metadata filter with explicit ownership predicates;
                // it never disables Tenant/SoftDelete, changes request context or changes PostgreSQL GUCs.
                var selections = context.ModerationPolicySelections.AsNoTracking();
                var graph = context.ModerationPolicyPublications.AsNoTracking();
                if (target.Scope == ConfigurationImportScope.Instance)
                {
                    selections = selections.IgnoreQueryFilters(["ModerationAuthority"]);
                    graph = graph.IgnoreQueryFilters(["ModerationAuthority"]);
                }
                var selection = await selections.SingleOrDefaultAsync(row =>
                    row.HostBindingId == host.Id && row.JurisdictionId == scope.Id, cancellationToken);
                if (selection is null)
                    continue;
                Guid? jurisdictionTenant = scope.Kind == ModerationScopeKind.Tenant ? scope.Id : null;
                if (selection.TrustDomainId != host.TrustDomainId
                    || selection.JurisdictionTenantId != jurisdictionTenant
                    || selection.Revision is < 1 or long.MaxValue)
                    throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
                var publication = await ModerationPolicyPublicationGraph.Include(graph.Where(row =>
                    row.HostBindingId == host.Id && row.TrustDomainId == host.TrustDomainId
                    && row.SolutionId == host.SolutionId && row.InstanceId == host.InstanceId
                    && row.JurisdictionTenantId == jurisdictionTenant))
                    .SingleOrDefaultAsync(row => row.Id == selection.LatestPublicationId, cancellationToken);
                if (publication is null || publication.PolicyId != selection.Id
                    || publication.Jurisdiction != scope || publication.Revision != selection.Revision
                    || publication.AdoptedRevision != publication.Revision || publication.PublishedAt > clock.GetUtcNow()
                    || jurisdictionTenant is { } owner && publication.TenantId != owner
                    || publication.AuthorityRevision is < 1 or long.MaxValue
                    || publication.QualificationRevision != publication.AuthorityRevision + 1)
                    throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);

                // An instance definition can originate in a different native tenant from this
                // request. Verify its actual source epoch rather than borrowing the request epoch.
                var epochs = context.ModerationQualificationEpochs.AsNoTracking();
                if (target.Scope == ConfigurationImportScope.Instance)
                    epochs = epochs.IgnoreQueryFilters(["ModerationQualification"]);
                var epoch = await epochs.SingleOrDefaultAsync(row => row.Id == publication.AuthoritySourceId
                    && row.HostBindingId == host.Id && row.TenantId == publication.TenantId, cancellationToken);
                if (!sources.Contains(publication.AuthoritySourceId)
                    || epoch is null || epoch.Revision < publication.QualificationRevision
                    || ModerationPolicyPublicationProjection.RestoreHistory(publication) is null)
                    throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
                result.Add(publication);
            }
            if (!CurrentTransaction(transaction.TransactionId) || context.ChangeTracker.HasChanges()
                || context.ModerationTrustDomainId != host.TrustDomainId
                || context.TenantFilterTenantId != requestTenant
                || !nativeInstanceOwner && !await nativeAuthority.ProviderTenantMatchesAsync(requestTenant!.Value, cancellationToken))
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
            if (nativeInstanceOwner)
                await RequireNativeInstanceExportAuthorityAsync(cancellationToken);
            return result.AsReadOnly();
        }
        finally
        {
            context.ModerationTrustDomainId = priorTrustDomain;
        }
    }

    private async Task RequireNativeInstanceExportAuthorityAsync(CancellationToken token)
    {
        var lease = instanceExportLease
            ?? throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        var scope = instanceExportScope
            ?? throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        var lifetime = instanceExportLifetime
            ?? throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        var authority = instanceExportAuthority
            ?? throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        var transaction = context.Database.CurrentTransaction;
        var trustDomain = context.ModerationTrustDomainId;

        RequireOriginalOwner();
        if (await admin.ResolveUserIdAsync(token) != scope.Subject.PrincipalId
            || !await authority.IsInstanceAdministratorForCommitAsync(scope.Subject.PrincipalId, token))
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        RequireOriginalOwner();

        void RequireOriginalOwner()
        {
            lease.RequireOriginalOwner(context);
            scope.RequireOwner(context);
            if (machine.IsMachineCaller || !context.IsConfigurationInstanceImportOwner
                || scope.Purpose != HumanAdministratorCommitPurpose.ConfigurationImport
                || scope.TenantId is not null || lease.SelectedTenantId is not null
                || lease.PolicyPurpose is not (null or Explore.Application.Contracts.Operations.PolicyMutationPurpose.Standard)
                || context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                || !ReferenceEquals(lease.HumanScope, scope) || !ReferenceEquals(lease.HumanLifetime, lifetime)
                || !ReferenceEquals(context.HumanCommitAuditSubject, scope.Subject)
                || context.HumanCommitContextLease != context.ContextId
                || context.ChangeTracker.HasChanges() || context.ModerationTrustDomainId != trustDomain
                || !ReferenceEquals(context.Database.CurrentTransaction, transaction)
                || transaction is null || !CurrentTransaction(transaction.TransactionId))
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
            lease.RequireOriginalCredential(instanceExportHttp?.HttpContext, lifetime);
            if (!lifetime.IsValidAt(clock.GetUtcNow()))
                throw new UnauthorizedAccessException("The original instance-export credential lifetime has ended.");
        }
    }

    private async Task AcquireExportPrincipalAsync(
        ConfigurationImportScope scope, Guid tenantId, CancellationToken token)
    {
        var userId = await admin.ResolveUserIdAsync(token)
            ?? throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unauthenticated);
        await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, userId, token);
        if (!await context.Users.AsNoTracking().AnyAsync(row => row.Id == userId && !row.IsDeleted, token))
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        foreach (var id in await context.UserExternalLogins.AsNoTracking().Where(row => row.UserId == userId)
                     .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token))
            await RelationalEntityRowFence.AcquireGlobalAsync<UserExternalLogin>(context, id, token);
        var platform = await context.PlatformUserRoles.AsNoTracking().Where(row => row.UserId == userId)
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToArrayAsync(token);
        foreach (var grant in platform)
            await RelationalEntityRowFence.AcquireGlobalAsync<PlatformUserRole>(context, grant.Id, token);
        var roleIds = platform.Select(row => row.RoleId).Distinct().Order().ToArray();
        foreach (var roleId in roleIds)
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [roleId], token);
        var grantIds = platform.Select(row => row.Id).ToArray();
        var instanceAdministrator = await context.PlatformUserRoles.AsNoTracking().AnyAsync(row =>
            grantIds.Contains(row.Id) && roleIds.Contains(row.RoleId) && row.UserId == userId
            && !row.User.IsDeleted && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform
            && row.Role.MasterCode == "platform.admin", token);
        var tenantAdministrator = false;
        if (scope == ConfigurationImportScope.Tenant && !instanceAdministrator)
        {
            var memberships = await context.TenantUsers.AsNoTracking()
                .Where(row => row.TenantId == tenantId && row.UserId == userId)
                .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
            foreach (var id in memberships)
                await RelationalEntityRowFence.AcquireAsync<TenantUser>(context, tenantId, row => row.Id, id, token);
            var grants = await context.TenantUserRoleGrants.AsNoTracking()
                .Where(row => row.TenantId == tenantId && memberships.Contains(row.TenantUserId))
                .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToArrayAsync(token);
            foreach (var grant in grants)
                await RelationalEntityRowFence.AcquireAsync<TenantUserRoleGrant>(
                    context, tenantId, row => row.Id, grant.Id, token);
            foreach (var roleId in grants.Select(row => row.RoleId).Distinct().Order())
                await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [roleId], token);
            var tenantGrantIds = grants.Select(row => row.Id).ToArray();
            tenantAdministrator = await context.TenantUserRoleGrants.AsNoTracking().AnyAsync(row =>
                tenantGrantIds.Contains(row.Id) && row.TenantId == tenantId && memberships.Contains(row.TenantUserId)
                && row.TenantUser.UserId == userId && !row.TenantUser.User.IsDeleted && !row.TenantUser.IsDeleted
                && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active && row.RevokedAt == null
                && row.RoleId == (int)RoleEnum.TenantAdmin && row.RoleScopeId == (int)RoleScopeEnum.Tenant
                && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant, token);
        }
        if (!instanceAdministrator && !tenantAdministrator || await admin.ResolveUserIdAsync(token) != userId)
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, tenantId, token);
        var tenant = await context.Tenants.AsNoTracking().SingleOrDefaultAsync(row => row.Id == tenantId, token)
            ?? throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(context, [tenant.TenantStatusId], token);
        if (!await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == tenantId && row.TenantStatus.IsActiveState, token))
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
    }
}
