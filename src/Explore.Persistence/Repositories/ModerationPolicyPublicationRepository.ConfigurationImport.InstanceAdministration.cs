using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using Explore.Application.Features.Moderation.Policy;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationPolicyPublicationRepository
{
    public async Task<ModerationConfigurationImportDestination> AcquireInstanceConfigurationImportDestinationAsync(
        Guid targetTenantId, Guid? baselinePublicationId, CancellationToken cancellationToken = default)
    {
        var transaction = context.Database.CurrentTransaction
            ?? throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        var priorTrustDomain = context.ModerationTrustDomainId;
        try
        {
            var (_, destination) = await AcquireInstanceImportDestinationAsync(
                targetTenantId, baselinePublicationId, cancellationToken);
            return destination;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            context.ModerationTrustDomainId = priorTrustDomain;
        }
    }

    public async Task<ModerationPolicyPublication> ApplyInstanceConfigurationImportInCurrentTransactionAsync(
        Guid operationId, Guid targetTenantId, ModerationConfigurationDocument incoming,
        ModerationConfigurationImportPreview preview, CancellationToken cancellationToken = default)
    {
        var transaction = context.Database.CurrentTransaction
            ?? throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        var requestContext = context.TenantContext;
        var requestTenant = context.TenantFilterTenantId;
        var priorTrustDomain = context.ModerationTrustDomainId;
        try
        {
            if (!Version7(operationId) || preview is null)
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Invalid);
            var (authority, destination) = await AcquireInstanceImportDestinationAsync(
                targetTenantId, preview.BaselinePublicationId, cancellationToken);
            var fresh = ModerationConfigurationImportAdapter.Compose(destination, incoming, inventory, preview);
            if (fresh.Binding != preview.Binding || fresh.Reserve().Digest != preview.Reserve().Digest)
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
            if (!fresh.CanApply || fresh.Validation?.Compilation.Draft is not { } draft
                || draft.Proposed.RequestedState != ModerationPolicyRequestedState.Draft
                || draft.Proposed.Jurisdiction != destination.Jurisdiction)
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Invalid);

            var retained = destination.Current?.Profiles.Select(row => row.ProfileId).ToHashSet() ?? [];
            var allocated = fresh.Mapping.ProfileIds.Values.Where(id => !retained.Contains(id)).ToArray();
            if (allocated.Any(id => !Version7(id))
                || fresh.Mapping.ProfileIds.Values.Distinct().Count() != fresh.Mapping.ProfileIds.Count
                || !Version7(draft.Proposed.PolicyId) || !Version7(draft.Proposed.PublicationId))
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Invalid);
            // Identity uniqueness is global, including prepared/unadopted definitions and other
            // jurisdictions. These existence checks disclose no foreign definition to the caller.
            if (await context.Set<ModerationPolicyPublicationProfile>()
                    .IgnoreQueryFilters(["ModerationAuthority"]).AsNoTracking()
                    .AnyAsync(row => allocated.Contains(row.ProfileId), cancellationToken)
                || await context.ModerationPolicyPublications.IgnoreQueryFilters(["ModerationAuthority"])
                    .AsNoTracking().AnyAsync(row => row.Id == draft.Proposed.PublicationId
                        || row.OperationId == operationId
                        || destination.Selection == null && row.PolicyId == draft.Proposed.PolicyId, cancellationToken)
                || await context.ModerationPolicyPublicationCandidates.IgnoreQueryFilters(["ModerationAuthority"])
                    .AsNoTracking().AnyAsync(row => row.Id == draft.Proposed.PublicationId
                        || destination.Selection == null && row.PolicyId == draft.Proposed.PolicyId, cancellationToken))
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);

            if (!ReferenceEquals(transaction, context.Database.CurrentTransaction)
                || !CurrentTransaction(transaction.TransactionId) || context.ChangeTracker.HasChanges())
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
            var observedAt = clock.GetUtcNow();
            var request = new ModerationPolicyPublicationRequest(operationId, authority.Binding.Id,
                authority.Epoch.Id, authority.Epoch.Revision, ModerationPolicyPublicationProjection.WriterGeneration,
                destination.Current?.Id, draft.Proposed);
            var publication = Snapshot(request, draft, authority,
                ModerationPolicyPublicationProjection.Digest(request), observedAt, []);
            ModerationPolicySelection? selection = null;
            if (destination.Selection is { } selected)
            {
                // Acquisition read persisted values, not the preview's potentially tracked entities.
                var tracked = context.ChangeTracker.Entries<ModerationPolicySelection>()
                    .SingleOrDefault(entry => entry.Entity.Id == selected.Id);
                if (tracked is null)
                {
                    context.Attach(selected);
                    selection = selected;
                }
                else
                {
                    tracked.CurrentValues.SetValues(selected);
                    tracked.OriginalValues.SetValues(selected);
                    selection = tracked.Entity;
                }
            }
            context.ModerationPolicyPublications.Add(publication);
            if (selection is null)
                context.ModerationPolicySelections.Add(ModerationPolicySelection.Start(publication));
            else
                selection.Replace(publication, draft.Proposed.ExpectedRevision, destination.Current!.Id);
            authority.Epoch.Advance(request.ExpectedQualificationRevision);
            await context.SaveChangesAsync(cancellationToken);

            // Target ownership is distinct from provider/request identity. The ordinary provider
            // guard still checks the captured request tenant, never an impersonated target.
            if (!ReferenceEquals(transaction, context.Database.CurrentTransaction)
                || !CurrentTransaction(transaction.TransactionId)
                || !ReferenceEquals(requestContext, context.TenantContext)
                || requestTenant is not { } sourceTenant
                || !await new ModerationQualificationAuthority(context, admin, machine)
                    .ProviderTenantMatchesAsync(sourceTenant, cancellationToken)
                || context.ModerationTrustDomainId != authority.Binding.TrustDomainId
                || clock.GetUtcNow() < observedAt)
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
            return publication;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            context.ModerationTrustDomainId = priorTrustDomain;
        }
    }

    private async Task<(ModerationSourceAuthority Authority, ModerationConfigurationImportDestination Destination)>
        AcquireInstanceImportDestinationAsync(Guid targetTenantId, Guid? baselinePublicationId, CancellationToken token)
    {
        var transaction = context.Database.CurrentTransaction;
        var requestContext = context.TenantContext;
        var providerAuthority = new ModerationQualificationAuthority(context, admin, machine);
        if (transaction is null || !CurrentTransaction(transaction.TransactionId)
            || machine.IsMachineCaller || context.TenantFilterTenantId is not { } requestTenant
            || !Version7(requestTenant) || !Version7(targetTenantId)
            || !await providerAuthority.ProviderTenantMatchesAsync(requestTenant, token))
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        if (context.ChangeTracker.HasChanges())
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
        if (baselinePublicationId is { } id && !Version7(id))
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Invalid);

        // This is mutation qualification, independent of read-only export. Preserve native writer
        // order: humans, logins, platform grants, roles, host, service principal, tenants/statuses,
        // source epochs, then the jurisdiction slot. Every fence belongs to the original transaction.
        var userId = await admin.ResolveUserIdAsync(token)
            ?? throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unauthenticated);
        await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, userId, token);
        if (!await context.Users.AsNoTracking().AnyAsync(row => row.Id == userId && !row.IsDeleted, token))
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        foreach (var login in await context.UserExternalLogins.AsNoTracking().Where(row => row.UserId == userId)
                     .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token))
            await RelationalEntityRowFence.AcquireGlobalAsync<UserExternalLogin>(context, login, token);
        var grants = await context.PlatformUserRoles.AsNoTracking().Where(row => row.UserId == userId)
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToArrayAsync(token);
        foreach (var grant in grants)
            await RelationalEntityRowFence.AcquireGlobalAsync<PlatformUserRole>(context, grant.Id, token);
        var roleIds = grants.Select(row => row.RoleId).Distinct().Order().ToArray();
        foreach (var role in roleIds)
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [role], token);
        var grantIds = grants.Select(row => row.Id).ToArray();
        if (!await context.PlatformUserRoles.AsNoTracking().AnyAsync(row => grantIds.Contains(row.Id)
                && roleIds.Contains(row.RoleId) && row.UserId == userId && !row.User.IsDeleted
                && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform && row.Role.MasterCode == "platform.admin", token)
            || await admin.ResolveUserIdAsync(token) != userId)
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);

        var discovered = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token)
            ?? throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, discovered.Id, token);
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        if (host is null || host.Id != discovered.Id || host.TrustDomainId != discovered.TrustDomainId
            || host.SolutionId != discovered.SolutionId || host.InstanceId != discovered.InstanceId
            || host.MaintenanceServicePrincipalId != discovered.MaintenanceServicePrincipalId)
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
        await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(context, host.MaintenanceServicePrincipalId, token);
        if (!await context.ServicePrincipals.AsNoTracking().AnyAsync(
                row => row.Id == host.MaintenanceServicePrincipalId && !row.IsDeleted, token))
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
        foreach (var tenantId in new[] { requestTenant, targetTenantId }.Distinct().Order())
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, tenantId, token);
            var tenant = await context.Tenants.AsNoTracking().SingleOrDefaultAsync(row => row.Id == tenantId, token)
                ?? throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
            await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(context, [tenant.TenantStatusId], token);
            if (!await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == tenantId
                    && row.TenantStatusId == tenant.TenantStatusId && row.TenantStatus.IsActiveState, token))
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        }
        // These are metadata filters, not provider tenant bypass. The replacement predicate
        // admits exactly this host's request and target epochs after live native root qualification.
        var epochQuery = context.ModerationQualificationEpochs.IgnoreQueryFilters(["ModerationQualification"])
            .AsNoTracking().Where(row => row.HostBindingId == host.Id
                && (row.TenantId == requestTenant || row.TenantId == targetTenantId));
        var discoveredEpochs = await epochQuery.OrderBy(row => row.Id).ToArrayAsync(token);
        if (discoveredEpochs.Length != (requestTenant == targetTenantId ? 1 : 2))
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
        foreach (var source in discoveredEpochs)
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(context, source.Id, token);
        var epochs = await epochQuery.OrderBy(row => row.Id).ToArrayAsync(token);
        if (!epochs.Select(row => (row.Id, row.TenantId)).SequenceEqual(
                discoveredEpochs.Select(row => (row.Id, row.TenantId)))
            || epochs.Any(row => row.Revision is < 1 or long.MaxValue))
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
        var epoch = epochs.Single(row => row.TenantId == targetTenantId);
        var trackedEpoch = context.ChangeTracker.Entries<ModerationQualificationEpoch>()
            .SingleOrDefault(entry => entry.Entity.Id == epoch.Id);
        if (trackedEpoch is null)
            context.Attach(epoch);
        else
        {
            trackedEpoch.CurrentValues.SetValues(epoch);
            trackedEpoch.OriginalValues.SetValues(epoch);
            epoch = trackedEpoch.Entity;
        }
        context.ModerationTrustDomainId = host.TrustDomainId;
        var instance = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId);
        var jurisdiction = AuthorityScope.Tenant(instance, targetTenantId);
        await using var slot = await AcquireSlotAsync(host.Id, jurisdiction, token);
        var selection = await context.ModerationPolicySelections.IgnoreQueryFilters(["ModerationAuthority"])
            .AsNoTracking().SingleOrDefaultAsync(row => row.HostBindingId == host.Id
                && row.JurisdictionId == targetTenantId, token);
        ModerationPolicyPublication? current = null;
        ModerationPolicyPublication? baseline = null;
        if (selection is not null)
        {
            if (selection.TrustDomainId != host.TrustDomainId || selection.JurisdictionTenantId != targetTenantId
                || selection.Revision is < 1 or long.MaxValue)
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
            var graph = ModerationPolicyPublicationGraph.Include(context.ModerationPolicyPublications
                .IgnoreQueryFilters(["ModerationAuthority"]).AsNoTracking().Where(row =>
                    row.HostBindingId == host.Id && row.TrustDomainId == host.TrustDomainId
                    && row.SolutionId == host.SolutionId && row.InstanceId == host.InstanceId
                    && row.TenantId == targetTenantId && row.JurisdictionTenantId == targetTenantId
                    && row.PolicyId == selection.Id));
            current = await graph.SingleOrDefaultAsync(row => row.Id == selection.LatestPublicationId, token);
            baselinePublicationId ??= current?.Id;
            var ancestor = current;
            var revision = selection.Revision;
            var activeFound = selection.ActivePublicationId is null;
            // Validate the whole adopted chain, not just the requested merge baseline. A
            // strictly descending revision both proves ancestry and rejects cycles.
            while (ancestor is not null)
            {
                if (ancestor.Jurisdiction != jurisdiction || ancestor.Revision != revision
                    || ancestor.AdoptedRevision != revision || ancestor.PublishedAt > clock.GetUtcNow()
                    || ancestor.AuthoritySourceId != epoch.Id || ancestor.AuthorityRevision is < 1 or long.MaxValue
                    || ancestor.QualificationRevision != ancestor.AuthorityRevision + 1
                    || ancestor.QualificationRevision > epoch.Revision
                    || ModerationPolicyPublicationProjection.RestoreHistory(ancestor) is null)
                    throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
                if (ancestor.Id == baselinePublicationId) baseline = ancestor;
                if (ancestor.Id == selection.ActivePublicationId)
                {
                    if (!ancestor.PublishedActive)
                        throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
                    activeFound = true;
                }
                if (revision == 1)
                {
                    if (ancestor.PreviousPublicationId is not null)
                        throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
                    break;
                }
                if (ancestor.PreviousPublicationId is not { } predecessor)
                    throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
                ancestor = await graph.SingleOrDefaultAsync(row => row.Id == predecessor, token);
                revision--;
            }
            if (current is null || ancestor is null || !activeFound)
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
            if (baseline is null)
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
        }
        else if (baselinePublicationId is not null)
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);

        if (!ReferenceEquals(transaction, context.Database.CurrentTransaction)
            || !CurrentTransaction(transaction.TransactionId)
            || !ReferenceEquals(requestContext, context.TenantContext)
            || context.ChangeTracker.HasChanges() || context.ModerationTrustDomainId != host.TrustDomainId
            || !await providerAuthority.ProviderTenantMatchesAsync(requestTenant, token))
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        return (new(userId, host, epoch, instance, jurisdiction, true, [userId], [userId]),
            new(userId, targetTenantId, jurisdiction, host, epoch, selection, current, baseline,
                await ImportAdministrationBindingAsync(userId, token), epochs.Single(row => row.TenantId == requestTenant)));
    }
}
