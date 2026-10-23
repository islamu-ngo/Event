using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationPolicyPublicationRepository
{
    private async Task<string> ImportAdministrationBindingAsync(Guid principal, CancellationToken token) =>
        ModerationPolicyPublicationProjection.Digest(new
        {
            Principal = principal,
            Logins = await context.UserExternalLogins.AsNoTracking().Where(row => row.UserId == principal)
                .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token),
            PlatformGrants = await context.PlatformUserRoles.AsNoTracking().Where(row => row.UserId == principal)
                .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId, row.Role.MasterCode, row.Role.RoleScopeId })
                .ToArrayAsync(token)
        });

    public async Task<Tenant> AcquireInstanceConfigurationImportTargetAsync(
        string slug, CancellationToken cancellationToken = default)
    {
        var transaction = context.Database.CurrentTransaction
            ?? throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        try
        {
            if (string.IsNullOrWhiteSpace(slug) || slug.Length > 200)
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Invalid);
            var tenant = await context.Tenants.AsNoTracking().SingleOrDefaultAsync(
                row => row.Slug == slug && row.TenantStatus.IsActiveState, cancellationToken)
                ?? throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
            _ = await AcquireInstanceConfigurationImportDestinationAsync(tenant.Id, null, cancellationToken);
            if (!await context.Tenants.AsNoTracking().AnyAsync(row =>
                    row.Id == tenant.Id && row.Slug == slug && row.TenantStatus.IsActiveState, cancellationToken))
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
            return tenant;
        }
        catch
        {
            if (ReferenceEquals(transaction, context.Database.CurrentTransaction))
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IModerationConfigurationImportCompletion> ApplyConfigurationImportSetInCurrentTransactionAsync(
        ConfigurationImportTarget target, IReadOnlyList<ModerationConfigurationImportSource> sources,
        ModerationConfigurationImportReservationSet reservation, CancellationToken cancellationToken = default)
    {
        var transaction = context.Database.CurrentTransaction
            ?? throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
        var requestContext = context.TenantContext;
        var requestTenant = context.TenantFilterTenantId;
        var trustDomain = context.ModerationTrustDomainId;
        var original = new List<(ModerationConfigurationReservedTarget Entry,
            ModerationConfigurationImportSource Source, ModerationConfigurationImportDestination Destination)>();
        var epochs = new Dictionary<Guid, (long Original, int Written)>();
        var completed = new List<(ModerationConfigurationReservedTarget Entry,
            ModerationConfigurationImportSource Source, ModerationConfigurationImportDestination Destination, string Binding)>();
        try
        {
            if (reservation is null || reservation.TargetAuthorityKey != target.AuthorityKey
                || sources.Count != reservation.Entries.Count || context.ChangeTracker.HasChanges())
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
            // Acquire and prove the entire original set before the first native write. No later
            // iteration is allowed to introduce a new target, mapping, allocation or revised head.
            foreach (var entry in reservation.Entries)
            {
                var source = sources.SingleOrDefault(source =>
                    source.SectionKey == entry.SectionKey && source.SourceName == entry.SourceName);
                if (source is null || source.TargetName != entry.TargetName)
                    throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
                var destination = await Acquire(entry, cancellationToken);
                var preview = ModerationConfigurationImportAdapter.ComposeReserved(
                    destination, source.Document, inventory, entry.Definition);
                if (!preview.CanApply || preview.Reserve().Digest != entry.Definition.Digest)
                    throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
                foreach (var observed in new[] { destination.Epoch, destination.RequestEpoch! })
                {
                    if (observed is null || epochs.TryGetValue(observed.Id, out var shared)
                        && shared.Original != observed.Revision)
                        throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
                    epochs[observed.Id] = (observed.Revision, 0);
                }
                original.Add((entry, source, FreezeEpoch(destination)));
            }
            foreach (var item in original)
            {
                EnsureOriginal();
                var fresh = await Acquire(item.Entry, cancellationToken);
                CheckEpoch(fresh, item.Destination);
                // Only the epoch revision may reflect this owner's preceding exact native writes.
                // Replacing it with the immutable original observation for comparison retains the
                // original binding of every other authority, history, selection and mapping fact.
                var normalized = ModerationConfigurationImportAdapter.ComposeReserved(
                    fresh with { Epoch = item.Destination.Epoch, RequestEpoch = item.Destination.RequestEpoch },
                    item.Source.Document, inventory, item.Entry.Definition);
                if (normalized.Reserve().Digest != item.Entry.Definition.Digest)
                    throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
                var currentPreview = ModerationConfigurationImportAdapter.ComposeReserved(
                    fresh, item.Source.Document, inventory, item.Entry.Definition);
                var publication = target.Scope == ConfigurationImportScope.Instance && item.Entry.TargetTenantId is { } tenantId
                    ? await ApplyInstanceConfigurationImportInCurrentTransactionAsync(
                        item.Entry.OperationId, tenantId, item.Source.Document, currentPreview, cancellationToken)
                    : await ApplyConfigurationImportInCurrentTransactionAsync(
                        item.Entry.OperationId, target, item.Source.Document, currentPreview, cancellationToken);
                var epoch = epochs[fresh.Epoch.Id];
                epochs[fresh.Epoch.Id] = (epoch.Original, checked(epoch.Written + 1));
                var after = await Acquire(item.Entry, cancellationToken);
                CheckEpoch(after, item.Destination);
                var unchangedAuthority = ModerationConfigurationImportAdapter.ComposeReserved(after with
                {
                    Epoch = item.Destination.Epoch, RequestEpoch = item.Destination.RequestEpoch,
                    Selection = item.Destination.Selection, Current = item.Destination.Current,
                    Baseline = item.Destination.Baseline
                }, item.Source.Document, inventory, item.Entry.Definition);
                var restored = after.Current is null ? null : ModerationPolicyPublicationProjection.RestoreHistory(after.Current);
                if (unchangedAuthority.Reserve().Digest != item.Entry.Definition.Digest
                    || after.Current?.Id != item.Entry.Definition.PublicationId
                    || after.Current.PolicyId != item.Entry.Definition.PolicyId
                    || after.Current.OperationId != item.Entry.OperationId
                    || after.Current.Id != publication.Id || after.Current.PublishedActive
                    || after.Current.PreviousPublicationId != item.Destination.Current?.Id
                    || after.Selection?.Revision != (item.Destination.Selection?.Revision ?? 0) + 1
                    || after.Selection?.ActivePublicationId != item.Destination.Selection?.ActivePublicationId
                    || after.Current.AuthoritySourceId != fresh.Epoch.Id
                    || after.Current.AuthorityRevision != epoch.Original + epoch.Written
                    || after.Current.QualificationRevision != epoch.Original + epoch.Written + 1
                    || after.Current.Profiles.Count != item.Entry.Definition.Profiles.Count
                    || after.Current.Profiles.Any(profile => !item.Entry.Definition.Profiles.Any(reserved =>
                        reserved.Code == profile.RuleCode && reserved.Revision == profile.RuleRevision
                        && reserved.ProfileId == profile.ProfileId))
                    || restored is null || currentPreview.Document is not { } expected
                    || !ModerationConfigurationDocumentDiff.Compare(expected, expected,
                        ModerationConfigurationDocumentProjection.FromCompiledDraft(restored)).Changes.IsEmpty)
                    throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
                var frozen = FreezeEpoch(after);
                var binding = ModerationConfigurationImportAdapter.ComposeReserved(
                    frozen, item.Source.Document, inventory, item.Entry.Definition).Binding;
                completed.Add((item.Entry, item.Source, frozen, binding));
            }
            var completion = new ConfigurationImportCompletion(Verify);
            await completion.VerifyBeforeCommitAsync(cancellationToken);
            return completion;
        }
        catch
        {
            if (ReferenceEquals(transaction, context.Database.CurrentTransaction))
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            context.ModerationTrustDomainId = trustDomain;
            context.TenantContext = requestContext;
        }

        void EnsureOriginal()
        {
            if (!ReferenceEquals(transaction, context.Database.CurrentTransaction)
                || !CurrentTransaction(transaction.TransactionId)
                || !ReferenceEquals(requestContext, context.TenantContext)
                || requestTenant != context.TenantFilterTenantId
                || trustDomain != context.ModerationTrustDomainId || context.ChangeTracker.HasChanges())
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
        }

        async Task<ModerationConfigurationImportDestination> Acquire(
            ModerationConfigurationReservedTarget entry, CancellationToken token)
        {
            EnsureOriginal();
            if (target.Scope == ConfigurationImportScope.Instance && entry.TargetTenantId is { } tenantId)
            {
                var destination = await AcquireInstanceConfigurationImportDestinationAsync(
                    tenantId, entry.Definition.BaselinePublicationId, token);
                if (!await context.Tenants.AsNoTracking().AnyAsync(row =>
                        row.Id == tenantId && row.Slug == entry.TargetName && row.TenantStatus.IsActiveState, token))
                    throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
                return destination;
            }
            if (entry.TargetTenantId != target.TenantId
                || entry.SectionKey != ModerationConfigurationImportSections.ForTarget(target))
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Denied);
            return await AcquireConfigurationImportDestinationAsync(target, entry.Definition.BaselinePublicationId, token);
        }

        ModerationConfigurationImportDestination FreezeEpoch(ModerationConfigurationImportDestination destination) =>
            destination with
            {
                Epoch = (ModerationQualificationEpoch)context.Entry(destination.Epoch).CurrentValues.ToObject(),
                RequestEpoch = (ModerationQualificationEpoch)context.Entry(destination.RequestEpoch!).CurrentValues.ToObject()
            };

        void CheckEpoch(ModerationConfigurationImportDestination live, ModerationConfigurationImportDestination observed)
        {
            foreach (var pair in new[] { (live.Epoch, observed.Epoch), (live.RequestEpoch!, observed.RequestEpoch!) })
            {
                if (pair.Item1 is null || pair.Item2 is null || pair.Item1.Id != pair.Item2.Id
                    || pair.Item1.HostBindingId != pair.Item2.HostBindingId || pair.Item1.TenantId != pair.Item2.TenantId
                    || !epochs.TryGetValue(pair.Item1.Id, out var expected)
                    || pair.Item1.Revision != checked(expected.Original + expected.Written))
                    throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
            }
        }

        async Task Verify(CancellationToken token)
        {
            try
            {
                EnsureOriginal();
                foreach (var item in completed)
                {
                    var live = await Acquire(item.Entry, token);
                    CheckEpoch(live, item.Destination);
                    var actual = ModerationConfigurationImportAdapter.ComposeReserved(
                        live with { Epoch = item.Destination.Epoch, RequestEpoch = item.Destination.RequestEpoch },
                        item.Source.Document, inventory, item.Entry.Definition);
                    if (actual.Binding != item.Binding)
                        throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
                }
                EnsureOriginal();
            }
            catch
            {
                if (ReferenceEquals(transaction, context.Database.CurrentTransaction))
                    await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
            finally
            {
                context.ModerationTrustDomainId = trustDomain;
                context.TenantContext = requestContext;
            }
        }
    }

    private sealed class ConfigurationImportCompletion(Func<CancellationToken, Task> verify)
        : IModerationConfigurationImportCompletion
    {
        public Task VerifyBeforeCommitAsync(CancellationToken cancellationToken) => verify(cancellationToken);
    }
}
