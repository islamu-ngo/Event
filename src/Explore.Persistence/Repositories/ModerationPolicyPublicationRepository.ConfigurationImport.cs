using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using Explore.Application.Features.Moderation.Policy;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationPolicyPublicationRepository
{
    public async Task<ModerationPolicyPublication> ApplyConfigurationImportReservationInCurrentTransactionAsync(
        Guid operationId, ConfigurationImportTarget target, ModerationConfigurationDocument incoming,
        ModerationConfigurationImportReservation reservation, IReadOnlyList<LegalDocument> ownedLegalDocuments,
        CancellationToken cancellationToken = default)
    {
        var transaction = context.Database.CurrentTransaction
            ?? throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        ModerationConfigurationImportPreview preview;
        try
        {
            if (!CurrentTransaction(transaction.TransactionId) || reservation is null)
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
            var owned = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (var document in ownedLegalDocuments)
            {
                if (target.Scope == ConfigurationImportScope.Tenant && document.TenantId != target.TenantId)
                    throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
                owned.Add(document);
                foreach (var version in document.Versions)
                {
                    owned.Add(version);
                    foreach (var source in version.Sources)
                        owned.Add(source);
                }
                foreach (var publication in document.Publications)
                    owned.Add(publication);
            }
            var pending = context.ChangeTracker.Entries()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .ToArray();
            if (pending.Any(entry => entry.State == EntityState.Deleted || !owned.Contains(entry.Entity)))
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
            if (pending.Length > 0)
                await context.SaveChangesAsync(cancellationToken);
            var destination = await AcquireConfigurationImportDestinationAsync(
                target, reservation.BaselinePublicationId, cancellationToken);
            preview = ModerationConfigurationImportAdapter.ComposeReserved(destination, incoming, inventory, reservation);
            if (preview.Binding != reservation.Binding || preview.Reserve().Digest != reservation.Digest)
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        return await ApplyConfigurationImportInCurrentTransactionAsync(
            operationId, target, incoming, preview, cancellationToken);
    }

    public async Task<ModerationConfigurationImportDestination> AcquireConfigurationImportDestinationAsync(
        ConfigurationImportTarget target, Guid? baselinePublicationId,
        CancellationToken cancellationToken = default)
    {
        var priorTrustDomain = context.ModerationTrustDomainId;
        try
        {
            var (_, destination) = await AcquireImportDestinationAsync(target, baselinePublicationId, cancellationToken);
            return destination;
        }
        finally
        {
            context.ModerationTrustDomainId = priorTrustDomain;
        }
    }

    public async Task<ModerationPolicyPublication> ApplyConfigurationImportInCurrentTransactionAsync(
        Guid operationId, ConfigurationImportTarget target, ModerationConfigurationDocument incoming,
        ModerationConfigurationImportPreview preview, CancellationToken cancellationToken = default)
    {
        var transaction = context.Database.CurrentTransaction
            ?? throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        var priorTrustDomain = context.ModerationTrustDomainId;
        try
        {
            if (!Version7(operationId) || preview is null)
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Invalid);
            var (authority, destination) = await AcquireImportDestinationAsync(
                target, preview.BaselinePublicationId, cancellationToken);
            var fresh = ModerationConfigurationImportAdapter.Compose(destination, incoming, inventory, preview);
            if (fresh.Binding != preview.Binding)
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
            if (!fresh.CanApply || fresh.Validation?.Compilation.Draft is not { } draft
                || draft.Proposed.RequestedState != ModerationPolicyRequestedState.Draft)
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Invalid);

            // Retained identities came from current history. Newly reserved identities cannot alias
            // any existing native profile, including a prepared but never adopted definition.
            var retained = destination.Current?.Profiles.Select(row => row.ProfileId).ToHashSet() ?? [];
            var allocated = fresh.Mapping.ProfileIds.Values.Where(id => !retained.Contains(id)).ToArray();
            if (await context.Set<ModerationPolicyPublicationProfile>().AsNoTracking()
                    .AnyAsync(row => allocated.Contains(row.ProfileId), cancellationToken)
                || await context.ModerationPolicyPublications.AsNoTracking().AnyAsync(
                    row => row.Id == draft.Proposed.PublicationId || row.OperationId == operationId, cancellationToken)
                || await context.ModerationPolicyPublicationCandidates.AsNoTracking().AnyAsync(
                    row => row.Id == draft.Proposed.PublicationId, cancellationToken))
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);

            if (!CurrentTransaction(transaction.TransactionId) || context.ChangeTracker.HasChanges())
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
                selection = await context.ModerationPolicySelections.SingleAsync(
                    row => row.Id == selected.Id, cancellationToken);
                // A clean tracked instance from a prior preview must not hide a newer persisted revision.
                await context.Entry(selection).ReloadAsync(cancellationToken);
                if (selection.Revision != selected.Revision
                    || selection.LatestPublicationId != selected.LatestPublicationId
                    || selection.ActivePublicationId != selected.ActivePublicationId)
                    throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
            }
            context.ModerationPolicyPublications.Add(publication);
            if (selection is null)
                context.ModerationPolicySelections.Add(ModerationPolicySelection.Start(publication));
            else
                selection.Replace(publication, draft.Proposed.ExpectedRevision, destination.Current!.Id);
            authority.Epoch.Advance(request.ExpectedQualificationRevision);
            await context.SaveChangesAsync(cancellationToken);
            if (!CurrentTransaction(transaction.TransactionId)
                || !await new ModerationQualificationAuthority(context, admin, machine)
                    .ProviderTenantMatchesAsync(authority.Tenant.Id, cancellationToken)
                || context.ModerationTrustDomainId != authority.Binding.TrustDomainId
                || clock.GetUtcNow() < observedAt)
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
            return publication;
        }
        catch
        {
            // A caller cannot catch a participant rejection and commit earlier import sections.
            // No nested transaction, independent commit, savepoint or foreign tracker flush exists.
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            context.ModerationTrustDomainId = priorTrustDomain;
        }
    }

    private async Task<(ModerationSourceAuthority Authority, ModerationConfigurationImportDestination Destination)>
        AcquireImportDestinationAsync(
            ConfigurationImportTarget target, Guid? baselinePublicationId, CancellationToken token)
    {
        var transaction = context.Database.CurrentTransaction;
        if (transaction is null || !CurrentTransaction(transaction.TransactionId)
            || machine.IsMachineCaller || target is null
            || target.Scope is not (ConfigurationImportScope.Instance or ConfigurationImportScope.Tenant)
            || context.TenantFilterTenantId is not { } tenantId
            || target.Scope == ConfigurationImportScope.Tenant && target.TenantId != tenantId)
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        if (context.ChangeTracker.HasChanges())
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
        if (baselinePublicationId is { } id && !Version7(id))
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Invalid);

        // This unfenced discovery supplies only a candidate scope. The ordinary native authority
        // owner reacquires humans, roles, host, service principal, tenant and epoch under its fences.
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token)
            ?? throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
        var instance = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId);
        var jurisdiction = target.Scope == ConfigurationImportScope.Instance
            ? instance : AuthorityScope.Tenant(instance, tenantId);
        var authority = await new ModerationQualificationAuthority(context, admin, machine)
            .AcquireAsync(jurisdiction, token)
            ?? throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        if (authority.Binding.Id != host.Id)
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
        await context.Entry(authority.Epoch).ReloadAsync(token);
        if (authority.Epoch.Revision is < 1 or long.MaxValue
            || authority.Epoch.HostBindingId != authority.Binding.Id || authority.Epoch.TenantId != tenantId)
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);

        await using var slot = await AcquireSlotAsync(authority.Binding.Id, jurisdiction, token);
        var selection = await context.ModerationPolicySelections.AsNoTracking().SingleOrDefaultAsync(
            row => row.HostBindingId == authority.Binding.Id && row.JurisdictionId == jurisdiction.Id, token);
        ModerationPolicyPublication? current = null;
        ModerationPolicyPublication? baseline = null;
        if (selection is not null)
        {
            current = await Graph().SingleOrDefaultAsync(row => row.Id == selection.LatestPublicationId, token);
            if (current is null || !Owned(current) || selection.Revision != current.Revision
                || selection.TrustDomainId != authority.Binding.TrustDomainId
                || selection.Revision is < 1 or long.MaxValue)
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
            baselinePublicationId ??= current.Id;
            // Validate every adopted ancestor and the independently active pointer, including
            // history older than the merge baseline. A descending revision rejects cycles.
            var ancestor = current;
            var revision = selection.Revision;
            var activeFound = selection.ActivePublicationId is null;
            while (ancestor is not null)
            {
                if (!Owned(ancestor) || ancestor.Revision != revision
                    || ancestor.AuthoritySourceId != authority.Epoch.Id
                    || ancestor.AuthorityRevision is < 1 or long.MaxValue
                    || ancestor.QualificationRevision != ancestor.AuthorityRevision + 1
                    || ancestor.QualificationRevision > authority.Epoch.Revision
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
                ancestor = await Graph().SingleOrDefaultAsync(row => row.Id == predecessor, token);
                revision--;
            }
            if (ancestor is null || !activeFound)
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
            if (baseline is null)
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
        }
        else if (baselinePublicationId is not null)
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
        if (!CurrentTransaction(transaction.TransactionId)
            || !await new ModerationQualificationAuthority(context, admin, machine).ProviderTenantMatchesAsync(tenantId, token))
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        return (authority, new(authority.UserId, tenantId, jurisdiction, authority.Binding, authority.Epoch,
            selection, current, baseline, await ImportAdministrationBindingAsync(authority.UserId, token), authority.Epoch));

        bool Owned(ModerationPolicyPublication publication) =>
            publication.HostBindingId == authority.Binding.Id
            && publication.TrustDomainId == authority.Binding.TrustDomainId
            && publication.SolutionId == authority.Binding.SolutionId
            && publication.InstanceId == authority.Binding.InstanceId
            && publication.TenantId == tenantId && publication.Jurisdiction == jurisdiction
            && publication.PolicyId == selection!.Id && publication.AdoptedRevision == publication.Revision
            && publication.Revision >= 1 && publication.PublishedAt <= clock.GetUtcNow();
    }
}
