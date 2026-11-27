using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
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

    public Task<IReadOnlyList<ModerationPolicyPublication>> AcquirePublicManifestExportAsync(
        ConfigurationManifestExportScope scope, CancellationToken cancellationToken)
    {
        if (scope is not (ConfigurationManifestExportScope.Instance or ConfigurationManifestExportScope.InstanceAndTenants))
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        return AcquireConfigurationExportCoreAsync(scope, null,
            HumanAdministratorCommitPurpose.ConfigurationManifestExport, cancellationToken);
    }

    public Task<IReadOnlyList<ModerationPolicyPublication>> AcquireConfigurationExportAsync(
        ConfigurationImportTarget target, ConfigurationManifestExportScope? instanceScope,
        CancellationToken cancellationToken)
    {
        if (target is { Scope: ConfigurationImportScope.Instance, TenantId: null }
            && instanceScope is ConfigurationManifestExportScope.Instance or ConfigurationManifestExportScope.InstanceAndTenants)
            return AcquireConfigurationExportCoreAsync(instanceScope, null,
                HumanAdministratorCommitPurpose.ConfigurationImport, cancellationToken);
        throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
    }

    public Task<IReadOnlyList<ModerationPolicyPublication>> AcquireTenantPackageExportAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        return AcquireConfigurationExportCoreAsync(null, tenantId, null, cancellationToken);
    }

    private async Task<IReadOnlyList<ModerationPolicyPublication>> AcquireConfigurationExportCoreAsync(
        ConfigurationManifestExportScope? instanceScope, Guid? tenantTarget,
        HumanAdministratorCommitPurpose? instancePurpose, CancellationToken cancellationToken)
    {
        // Both modes run on a tenantless Global context: instance export under its native owner, the
        // tenant package read-only for exactly one tenant selected by explicit predicates below.
        var transaction = context.Database.CurrentTransaction;
        var requestTenant = context.TenantFilterTenantId;
        bool instanceRead = instancePurpose.HasValue;
        if (transaction is null || !CurrentTransaction(transaction.TransactionId)
            || machine.IsMachineCaller || requestTenant is not null
            || context.IsConfigurationInstanceImportOwner != (instancePurpose == HumanAdministratorCommitPurpose.ConfigurationImport)
            || !instanceRead && (tenantTarget is null || context.IsTenantFilterBypassed))
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        if (context.ChangeTracker.HasChanges())
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);

        // Definition export remains usable before moderation installation. Its read authority is
        // therefore acquired from native human/role rows, not a required installation epoch.
        if (instancePurpose is { } purpose)
            await RequireNativeInstanceExportAuthorityAsync(purpose, cancellationToken);
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (host is null)
        {
            if (!CurrentTransaction(transaction.TransactionId) || context.TenantFilterTenantId != requestTenant)
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
            if (instancePurpose is { } missingHostPurpose)
                await RequireNativeInstanceExportAuthorityAsync(missingHostPurpose, cancellationToken);
            return Array.Empty<ModerationPolicyPublication>();
        }
        // Row fences are write-privileged no-op updates that hold off concurrent writers; instance
        // export keeps them. The read-only tenant package relies on this serializable snapshot, and its
        // owner rejects the package when a second read returns different source publications.
        if (instanceRead)
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, host.Id, cancellationToken);
        var fencedHost = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (fencedHost is null || fencedHost.Id != host.Id || fencedHost.TrustDomainId != host.TrustDomainId
            || fencedHost.SolutionId != host.SolutionId || fencedHost.InstanceId != host.InstanceId
            || fencedHost.MaintenanceServicePrincipalId != host.MaintenanceServicePrincipalId)
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
        if (instanceRead)
            await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(
                context, host.MaintenanceServicePrincipalId, cancellationToken);
        if (!await context.ServicePrincipals.AsNoTracking().AnyAsync(
                row => row.Id == host.MaintenanceServicePrincipalId && !row.IsDeleted, cancellationToken))
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);

        var instance = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId);
        var scopes = new List<AuthorityScope>();
        if (instanceRead)
            scopes.Add(instance);
        if (instanceScope != ConfigurationManifestExportScope.Instance)
        {
            var tenantQuery = context.Tenants.AsNoTracking().IgnoreTenantFilter(instanceRead
                ? TenantFilterBypassReasons.InstanceConfigurationManifestExport
                : TenantFilterBypassReasons.ConfigurationManifestPackageExport);
            if (!instanceRead)
                tenantQuery = tenantQuery.Where(row => row.Id == tenantTarget);
            var candidates = tenantQuery.Where(row => row.TenantStatus.IsActiveState);
            var tenants = await candidates.OrderBy(row => row.Id)
                .Take(ConfigurationManifestValidator.MaximumTenantCount + 1).ToArrayAsync(cancellationToken);
            if (tenants.Length > ConfigurationManifestValidator.MaximumTenantCount || !instanceRead && tenants.Length != 1)
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
            foreach (var tenant in tenants)
            {
                if (instanceRead)
                {
                    await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, tenant.Id, cancellationToken);
                    await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(
                        context, [tenant.TenantStatusId], cancellationToken);
                }
                if (!await candidates.AnyAsync(row => row.Id == tenant.Id
                        && row.TenantStatusId == tenant.TenantStatusId, cancellationToken))
                    throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
                scopes.Add(AuthorityScope.Tenant(instance, tenant.Id));
            }
        }

        var result = new List<ModerationPolicyPublication>();
        var priorTrustDomain = context.ModerationTrustDomainId;
        try
        {
            context.ModerationTrustDomainId = host.TrustDomainId;
            // Preserve writer ordering: native source epochs precede jurisdiction slots. Discovery
            // yields only fence coordinates; selected ownership is checked again below.
            var discovery = context.ModerationPolicyPublications.AsNoTracking()
                .IgnoreQueryFilters(["ModerationAuthority"]);
            discovery = discovery.Where(row => row.HostBindingId == host.Id
                && row.TrustDomainId == host.TrustDomainId
                && row.SolutionId == host.SolutionId && row.InstanceId == host.InstanceId);
            var selections = context.ModerationPolicySelections.AsNoTracking()
                .IgnoreQueryFilters(["ModerationAuthority"]);
            if (instanceScope == ConfigurationManifestExportScope.Instance)
            {
                // Fence only the selected instance publication's provenance, not all historical
                // instance source epochs. The slot read below rechecks the selection after fencing.
                discovery = discovery.Where(row => row.JurisdictionTenantId == null
                    && selections.Any(selection => selection.HostBindingId == host.Id
                        && selection.JurisdictionId == instance.Id && selection.JurisdictionTenantId == null
                        && selection.LatestPublicationId == row.Id));
            }
            else if (instanceRead)
            {
                var tenantIds = scopes.Where(scope => scope.Kind == ModerationScopeKind.Tenant)
                    .Select(scope => scope.Id).ToArray();
                discovery = discovery.Where(row => row.JurisdictionTenantId == null
                    || row.JurisdictionTenantId.HasValue && tenantIds.Contains(row.JurisdictionTenantId.Value));
            }
            else
                discovery = discovery.Where(row => row.JurisdictionTenantId == tenantTarget);
            var sources = await discovery
                .Select(row => row.AuthoritySourceId).Distinct().Order().ToArrayAsync(cancellationToken);
            if (instanceRead)
                foreach (var source in sources)
                    await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(context, source, cancellationToken);
            foreach (var scope in scopes)
            {
                await using var slot = await AcquireSlotAsync(host.Id, scope, cancellationToken);
                // Only definition metadata crosses tenant scope, after native instance authority.
                // This replaces the exact named metadata filter with explicit ownership predicates;
                // it never disables Tenant/SoftDelete, changes request context or changes PostgreSQL GUCs.
                var graph = context.ModerationPolicyPublications.AsNoTracking()
                    .IgnoreQueryFilters(["ModerationAuthority"]);
                Guid? jurisdictionTenant = scope.Kind == ModerationScopeKind.Tenant ? scope.Id : null;
                var selectionQuery = selections.Where(row => row.HostBindingId == host.Id && row.JurisdictionId == scope.Id);
                if (instanceScope == ConfigurationManifestExportScope.Instance)
                    selectionQuery = selectionQuery.Where(row => row.JurisdictionTenantId == null);
                var selection = await selectionQuery.SingleOrDefaultAsync(cancellationToken);
                if (selection is null)
                    continue;
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
                var epochs = context.ModerationQualificationEpochs.AsNoTracking()
                    .IgnoreQueryFilters(["ModerationQualification"]);
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
                || context.TenantFilterTenantId != requestTenant)
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
            if (instancePurpose is { } finalPurpose)
                await RequireNativeInstanceExportAuthorityAsync(finalPurpose, cancellationToken);
            return result.AsReadOnly();
        }
        finally
        {
            context.ModerationTrustDomainId = priorTrustDomain;
        }
    }

    private async Task RequireNativeInstanceExportAuthorityAsync(
        HumanAdministratorCommitPurpose purpose, CancellationToken token)
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
            if (machine.IsMachineCaller || scope.Purpose != purpose
                || context.IsConfigurationInstanceImportOwner != (purpose == HumanAdministratorCommitPurpose.ConfigurationImport)
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
}
