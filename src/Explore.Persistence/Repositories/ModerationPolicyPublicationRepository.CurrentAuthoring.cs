using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationPolicyPublicationRepository
{
    public async Task<ModerationCurrentPolicyReadAcquisition> AcquireCurrentAuthoringAsync(
        ModerationCurrentPolicyReadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Scope is not (ModerationScopeKind.Instance or ModerationScopeKind.Tenant))
            return new(ModerationCurrentPolicyReadOutcome.Invalid);
        if (machine.IsMachineCaller) return new(ModerationCurrentPolicyReadOutcome.Denied);
        var human = await admin.ResolveUserIdAsync(cancellationToken);
        if (human is null) return new(ModerationCurrentPolicyReadOutcome.Unauthenticated);
        if (human != request.HumanId) return new(ModerationCurrentPolicyReadOutcome.Denied);
        if (!context.Database.IsRelational() || context.Database.CurrentTransaction is not null
            || context.ChangeTracker.HasChanges() || context.IsTenantFilterBypassed
            || request.RequestTenantId == Guid.Empty || context.TenantFilterTenantId != request.RequestTenantId
            || context.TenantContext is null)
            return new(ModerationCurrentPolicyReadOutcome.Unavailable);

        var originalTenant = context.TenantContext;
        var originalTrust = context.ModerationTrustDomainId;
        var connection = context.Database.GetDbConnection();
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var handedOff = false;
        IAsyncDisposable? slot = null;
        try
        {
            var qualification = new ModerationQualificationAuthority(context, admin, machine);
            if (!await qualification.ProviderTenantMatchesAsync(request.RequestTenantId, cancellationToken))
                return new(ModerationCurrentPolicyReadOutcome.Unavailable);
            var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (host is null || originalTrust is { } trust && trust != host.TrustDomainId)
                return new(ModerationCurrentPolicyReadOutcome.Unavailable);
            var instance = AuthorityScope.Instance(AuthorityScope.Solution(
                AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId);
            var jurisdiction = request.Scope == ModerationScopeKind.Instance
                ? instance : AuthorityScope.Tenant(instance, request.RequestTenantId);
            if (!await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(row =>
                    row.HostBindingId == host.Id && row.TenantId == request.RequestTenantId, cancellationToken))
                return new(ModerationCurrentPolicyReadOutcome.Unavailable);
            context.ModerationTrustDomainId = host.TrustDomainId;
            var plannedPublication = await EventAuthorityProviderOperations.ReadWithoutRetainingLocksAsync(context,
                source => source.ModerationPolicySelections.AsNoTracking()
                    .Where(row => row.HostBindingId == host.Id && row.JurisdictionId == jurisdiction.Id)
                    .Select(row => (Guid?)row.LatestPublicationId).SingleOrDefaultAsync(cancellationToken), cancellationToken);
            var authority = await qualification.AcquireAsync(jurisdiction, plannedPublication, cancellationToken);
            if (authority is null) return new(ModerationCurrentPolicyReadOutcome.Denied);
            if (authority.UserId != human || authority.Binding.Id != host.Id
                || authority.Binding.TrustDomainId != host.TrustDomainId
                || authority.Binding.SolutionId != host.SolutionId || authority.Binding.InstanceId != host.InstanceId
                || authority.Epoch.Revision is < 1 or long.MaxValue)
                return new(ModerationCurrentPolicyReadOutcome.Unavailable);

            // Native acquisition precedes the jurisdiction slot, matching Draft/preparation writers.
            slot = await AcquireSlotAsync(host.Id, jurisdiction, cancellationToken);
            var selected = await ReadCurrentAuthoringSelectionAsync(
                transaction.TransactionId, authority.Binding, jurisdiction, cancellationToken);
            if (!selected.Valid || selected.Publication?.Id != plannedPublication)
                return new(ModerationCurrentPolicyReadOutcome.Unavailable);
            var lease = new CurrentAuthoringReadLease(this, context, admin, machine, qualification, transaction, connection,
                originalTenant, originalTrust, request, authority, jurisdiction,
                selected.Selection, selected.Publication, inventory);
            var outcome = await lease.RevalidateAsync(cancellationToken);
            if (outcome != ModerationCurrentPolicyReadOutcome.Allowed)
                return new(outcome);
            handedOff = true;
            return new(ModerationCurrentPolicyReadOutcome.Allowed, lease);
        }
        finally
        {
            // Named locks are transaction-bound; disposing this handle does not release the slot.
            if (slot is not null) await slot.DisposeAsync();
            if (!handedOff)
            {
                context.ModerationTrustDomainId = originalTrust;
                context.TenantContext = originalTenant;
                await transaction.DisposeAsync();
            }
        }
    }

    private async Task<(bool Valid, ModerationPolicySelection? Selection, ModerationPolicyPublication? Publication)>
        ReadCurrentAuthoringSelectionAsync(
            Guid transactionId, ModerationHostBinding host, AuthorityScope jurisdiction, CancellationToken token)
    {
        var selection = await context.ModerationPolicySelections.AsNoTracking().SingleOrDefaultAsync(row =>
            row.HostBindingId == host.Id && row.JurisdictionId == jurisdiction.Id, token);
        if (selection is null) return (true, null, null);
        Guid? tenant = jurisdiction.Kind == ModerationScopeKind.Tenant ? jurisdiction.Id : null;
        if (selection.TrustDomainId != host.TrustDomainId || selection.JurisdictionTenantId != tenant
            || !Version7(selection.Id) || !Version7(selection.LatestPublicationId)
            || selection.Revision is < 1 or long.MaxValue)
            return (false, null, null);
        var publication = await definitions.AcquireSelectedAsync(
            transactionId, host.Id, jurisdiction, selection.Id, false, token);
        if (publication is null || publication.Id != selection.LatestPublicationId
            || publication.Revision != selection.Revision || publication.AdoptedRevision != selection.Revision
            || publication.HostBindingId != host.Id || publication.TrustDomainId != host.TrustDomainId
            || publication.SolutionId != host.SolutionId || publication.InstanceId != host.InstanceId
            || publication.PolicyId != selection.Id || publication.Jurisdiction != jurisdiction
            || publication.JurisdictionTenantId != tenant || tenant is { } owner && publication.TenantId != owner
            || !Version7(publication.TenantId) || !Version7(publication.AuthoritySourceId)
            || publication.AuthorityRevision is < 1 or long.MaxValue
            || publication.QualificationRevision != publication.AuthorityRevision + 1
            || publication.PublishedAt > clock.GetUtcNow())
            return (false, null, null);
        return (true, selection, publication);
    }

    private sealed class CurrentAuthoringReadLease(
        ModerationPolicyPublicationRepository owner, ExploreDbContext context,
        IAdminContext admin, IMachinePrincipalAccessor machine,
        ModerationQualificationAuthority qualification, IDbContextTransaction transaction, DbConnection connection,
        ITenantContext originalTenant, Guid? originalTrust, ModerationCurrentPolicyReadRequest request,
        ModerationSourceAuthority original, AuthorityScope jurisdiction,
        ModerationPolicySelection? selection, ModerationPolicyPublication? publication,
        ModerationCapabilityRegistry inventory) : IModerationCurrentPolicyReadLease
    {
        private bool disposed;
        private readonly long epochRevision = original.Epoch.Revision;
        private readonly Guid epochId = original.Epoch.Id;
        private readonly Guid? selectedId = selection?.Id;
        private readonly Guid? latestId = selection?.LatestPublicationId;
        private readonly long selectedRevision = selection?.Revision ?? 0;

        public ModerationHostBinding Host { get; } = original.Binding;
        public ModerationQualificationEpoch Epoch { get; } = original.Epoch;
        public AuthorityScope Jurisdiction { get; } = jurisdiction;
        public ModerationPolicySelection? Selection { get; } = selection;
        public ModerationPolicyPublication? Publication { get; } = publication;
        public ModerationCapabilityRegistry Inventory { get; } = inventory;

        internal async Task<ModerationConsumerEvidenceSession?> AcquireMaterialEvidenceAsync(
            DateTimeOffset observedAt, CancellationToken cancellationToken)
        {
            if (Publication is null || await RevalidateAsync(cancellationToken) != ModerationCurrentPolicyReadOutcome.Allowed)
                return null;
            // The first acquisition already owns this publication's nominated native sources.
            var native = original;
            var definition = await owner.definitions.ReadDefinitionAsync(native, transaction.TransactionId,
                Jurisdiction, Publication.PolicyId, Publication.Id, observedAt, cancellationToken);
            if (definition is null || definition.Policy.Publication.PublicationId != Publication.Id
                || definition.Policy.Publication.ProposedRevision != Publication.Revision
                || await RevalidateAsync(cancellationToken) != ModerationCurrentPolicyReadOutcome.Allowed)
                return null;
            return owner.BorrowConsumerEvidence(native, definition.Policy);
        }

        public async Task<ModerationCurrentPolicyReadOutcome> RevalidateAsync(CancellationToken cancellationToken)
        {
            if (!FrameCurrent()) return ModerationCurrentPolicyReadOutcome.Unavailable;
            if (machine.IsMachineCaller) return ModerationCurrentPolicyReadOutcome.Denied;
            var human = await admin.ResolveUserIdAsync(cancellationToken);
            if (human is null) return ModerationCurrentPolicyReadOutcome.Unauthenticated;
            if (human != original.UserId) return ModerationCurrentPolicyReadOutcome.Denied;
            if (!await qualification.ProviderTenantMatchesAsync(request.RequestTenantId, cancellationToken))
                return ModerationCurrentPolicyReadOutcome.Unavailable;

            // Reacquire, but never replace the original expected coordinates with the new observation.
            if (original.MaterialAuthority is { } plan)
            {
                var events = plan.Parents.Select(row => row.Id).Distinct().ToArray();
                var humans = original.FencedHumans.ToArray();
                var parents = await EventAuthorityProviderOperations.ReadOwnershipAsync(context,
                    request.RequestTenantId, events, cancellationToken);
                var assignments = await EventAuthorityProviderOperations.ReadAssignmentsAsync(context,
                    request.RequestTenantId, humans, events, cancellationToken);
                if (!parents.OrderBy(row => row.Id).SequenceEqual(plan.Parents.OrderBy(row => row.Id))
                    || !assignments.OrderBy(row => row.Id).SequenceEqual(plan.Assignments.OrderBy(row => row.Id)))
                    return ModerationCurrentPolicyReadOutcome.Unavailable;
            }
            var current = await qualification.AcquireAsync(Jurisdiction, Publication?.Id, cancellationToken);
            if (current is null) return ModerationCurrentPolicyReadOutcome.Denied;
            var epoch = await context.ModerationQualificationEpochs.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == epochId, cancellationToken);
            if (current.UserId != original.UserId || current.Binding.Id != Host.Id
                || current.Binding.TrustDomainId != Host.TrustDomainId
                || current.Binding.SolutionId != Host.SolutionId || current.Binding.InstanceId != Host.InstanceId
                || current.Binding.MaintenanceServicePrincipalId != Host.MaintenanceServicePrincipalId
                || current.Tenant.Id != request.RequestTenantId || current.Epoch.Id != epochId
                || epoch is null || epoch.HostBindingId != Host.Id || epoch.TenantId != request.RequestTenantId
                || epoch.Revision != epochRevision)
                return ModerationCurrentPolicyReadOutcome.Unavailable;
            var selected = await owner.ReadCurrentAuthoringSelectionAsync(
                transaction.TransactionId, Host, Jurisdiction, cancellationToken);
            if (!selected.Valid || selected.Selection?.Id != selectedId
                || selected.Selection?.LatestPublicationId != latestId
                || (selected.Selection?.Revision ?? 0) != selectedRevision)
                return ModerationCurrentPolicyReadOutcome.Unavailable;
            // Continued absence is compared just as strictly as an adopted latest pointer.
            return FrameCurrent() && !machine.IsMachineCaller
                && await admin.ResolveUserIdAsync(cancellationToken) == original.UserId
                && await qualification.ProviderTenantMatchesAsync(request.RequestTenantId, cancellationToken)
                ? ModerationCurrentPolicyReadOutcome.Allowed : ModerationCurrentPolicyReadOutcome.Unavailable;
        }

        private bool FrameCurrent() =>
            !disposed && ReferenceEquals(context.Database.CurrentTransaction, transaction)
            && ReferenceEquals(context.Database.GetDbConnection(), connection)
            && ReferenceEquals(transaction.GetDbTransaction().Connection, connection)
            && transaction.GetDbTransaction().IsolationLevel == IsolationLevel.Serializable
            && ReferenceEquals(context.TenantContext, originalTenant)
            && context.TenantFilterTenantId == request.RequestTenantId && !context.IsTenantFilterBypassed
            && context.ModerationTrustDomainId == Host.TrustDomainId && !context.ChangeTracker.HasChanges();

        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            context.ModerationTrustDomainId = originalTrust;
            context.TenantContext = originalTenant;
            await transaction.DisposeAsync();
        }
    }
}
