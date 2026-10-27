using System.Data;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Independent current-human status owner. Stable ledger coordinates name a candidate, never an
/// identity. No approval, finding, evidence, original text or current profile is read here.
/// </summary>
public sealed partial class ModerationNoticeStatusRepository(
    ExploreDbContext context, IAdminContext identity, IMachinePrincipalAccessor machine,
    TimeProvider clock, IPrivacyIdentityFenceAuthority privacy)
    : IModerationNoticeStatusRepository
{
    private ExploreDbContext Context => context;
    private TimeProvider Clock => clock;

    public Task<T> ExecuteSerializedAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        privacy.ExecuteSerializedAsync(operation, cancellationToken);

    public async Task<ModerationNoticeStatusAcquisition> AcquireAsync(
        ModerationNoticeStatusRequest request, CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || request.SubjectId == Guid.Empty
            || request.DecisionId == Guid.Empty || request.EffectId == Guid.Empty)
            return new(ModerationNoticeReadOutcome.Invalid);
        if (machine.IsMachineCaller || await identity.ResolveUserIdAsync(cancellationToken) != request.SubjectId)
            return new(ModerationNoticeReadOutcome.Unauthenticated);
        if (!context.Database.IsRelational()
            || context.Database.CurrentTransaction is not null && !context.IdentityFenceOwnsTransaction
            || context.ChangeTracker.HasChanges() || context.IsTenantFilterBypassed
            || context.ModerationTrustDomainId is not null || context.TenantFilterTenantId != request.TenantId)
            return new(ModerationNoticeReadOutcome.Unavailable);

        var ownsTransaction = !context.IdentityFenceOwnsTransaction;
        var transaction = ownsTransaction
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : context.Database.CurrentTransaction!;
        var handedOff = false;
        StatusLease? lease = null;
        try
        {
            if (!await ProviderTenantMatchesAsync(request.TenantId, cancellationToken))
                return new(ModerationNoticeReadOutcome.Unavailable);
            var host = await EventAuthorityProviderOperations.ReadWithoutRetainingLocksAsync(context,
                planning => planning.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken), cancellationToken);
            if (host is null) return new(ModerationNoticeReadOutcome.NotFound);
            context.ModerationTrustDomainId = host.TrustDomainId;
            var representativePlan = await DiscoverRepresentativePlanAsync(request, host, cancellationToken);
            // Original status metadata nominates sources only. Their early anchors precede
            // Tenant; the proof reader must qualify the same original representative again.
            await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, request.SubjectId, cancellationToken);
            var logins = await context.UserExternalLogins.AsNoTracking()
                .Where(row => row.UserId == request.SubjectId).OrderBy(row => row.Id)
                .Select(row => row.Id).ToArrayAsync(cancellationToken);
            foreach (var id in logins)
                await RelationalEntityRowFence.AcquireGlobalAsync<UserExternalLogin>(context, id, cancellationToken);
            var roles = representativePlan?.Assignments.Select(row => row.RoleId).Distinct().Order().ToArray() ?? [];
            foreach (var role in roles)
                await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [role], cancellationToken);
            await EventAuthoritySnapshotService.AcquirePermissionSourcesAsync(context, roles, cancellationToken);
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, host.Id, cancellationToken);
            foreach (var actor in (representativePlan?.Parents ?? [])
                .SelectMany(row => new Guid?[] { row.ActorId, row.OrganizerActorId })
                .Where(id => id.HasValue).Select(id => id!.Value).Distinct().Order())
                await RelationalEntityRowFence.AcquireGlobalAsync<Actor>(context, actor, cancellationToken);
            await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, request.TenantId, cancellationToken);
            var epoch = await context.ModerationQualificationEpochs.AsNoTracking().SingleOrDefaultAsync(
                row => row.HostBindingId == host.Id && row.TenantId == request.TenantId, cancellationToken);
            if (epoch is null) return new(ModerationNoticeReadOutcome.NotFound);
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(context, epoch.Id, cancellationToken);
            var caseId = await context.ModerationDecisions.AsNoTracking()
                .Where(row => row.Id == request.DecisionId
                    && (row.SubjectKind == ModerationSubjectKind.Human && row.SubjectId == request.SubjectId
                        || row.SubjectKind == ModerationSubjectKind.Content && row.ScopeTenantId == request.TenantId))
                .Select(row => (Guid?)row.CaseId)
                .SingleOrDefaultAsync(cancellationToken);
            if (caseId is null) return new(ModerationNoticeReadOutcome.NotFound);
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCase>(context, caseId.Value, cancellationToken);
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationDecision>(context, request.DecisionId, cancellationToken);
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationEffectIntent>(context, request.EffectId, cancellationToken);
            var occurrences = await context.ModerationNotices.AsNoTracking().Where(row => row.EffectId == request.EffectId
                    && (row.Audience == ModerationNoticeAudience.Subject
                        || row.Audience == ModerationNoticeAudience.ContentRepresentative)
                    && row.RecipientSubjectKind == (int)ModerationSubjectKind.Human
                    && row.RecipientSubjectId == request.SubjectId)
                .OrderBy(row => row.Id).Select(row => row.Id).Take(2).ToArrayAsync(cancellationToken);
            if (occurrences.Length > 1) return new(ModerationNoticeReadOutcome.Unavailable);
            foreach (var id in occurrences)
                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationNotice>(context, id, cancellationToken);
            await using var sourceFence = await RelationalNamedLock.AcquireTransactionAsync(
                context, $"moderation.notice:{request.EffectId:D}", cancellationToken);
            var acquiredAt = clock.GetUtcNow();
            if (!await AuthorityCurrentAsync(request, host, cancellationToken))
                return new(ModerationNoticeReadOutcome.NotFound);
            var entry = await ReadOwnedAsync(request, host, representativePlan, cancellationToken);
            if (entry is null) return new(ModerationNoticeReadOutcome.NotFound);
            lease = new StatusLease(this, transaction, ownsTransaction, request, host, acquiredAt, entry, representativePlan);
            if (await lease.RevalidateAsync(cancellationToken) != ModerationNoticeReadOutcome.Allowed)
                return new(ModerationNoticeReadOutcome.NotFound);
            var result = new ModerationNoticeStatusAcquisition(ModerationNoticeReadOutcome.Allowed, lease);
            handedOff = true;
            lease = null;
            return result;
        }
        finally
        {
            if (lease is not null)
                await lease.DisposeAsync();
            else if (!handedOff)
            {
                context.ModerationTrustDomainId = null;
                if (ownsTransaction) await transaction.DisposeAsync();
            }
        }
    }

    private Task<EventAuthorityProviderOperations.AuthorityPlan?> DiscoverRepresentativePlanAsync(
        ModerationNoticeStatusRequest request, ModerationHostBinding host, CancellationToken token) =>
        EventAuthorityProviderOperations.ReadWithoutRetainingLocksAsync<EventAuthorityProviderOperations.AuthorityPlan?>(
            context, async planning =>
            {
                var eventId = await planning.ModerationDecisions.AsNoTracking()
                    .Where(row => row.Id == request.DecisionId && row.SubjectKind == ModerationSubjectKind.Content
                        && row.ScopeTenantId == request.TenantId)
                    .Select(row => (Guid?)row.SubjectId).SingleOrDefaultAsync(token);
                if (eventId is null) return null;
                var source = await planning.ModerationNoticeSources.AsNoTracking()
                    .Where(row => row.EffectId == request.EffectId && row.DecisionId == request.DecisionId
                        && row.TrustDomainId == host.TrustDomainId && row.TenantId == request.TenantId)
                    .Select(row => new { row.StatusMetadataJson, row.StatusMetadataHash }).SingleOrDefaultAsync(token);
                var metadata = source is null ? null
                    : ModerationNoticeSource.StatusMetadata.Read(source.StatusMetadataJson, source.StatusMetadataHash);
                if (metadata is null || !metadata.ContentRepresentatives.Any(row => row.HumanId == request.SubjectId))
                    return null;
                return new(
                    await EventAuthorityProviderOperations.ReadOwnershipAsync(planning, request.TenantId, [eventId.Value], token),
                    await EventAuthorityProviderOperations.ReadAssignmentsAsync(planning, request.TenantId,
                        [request.SubjectId], [eventId.Value], token));
            }, token);

    private sealed record QualifiedStatus(ModerationNoticeStatusEntry Entry, DateTimeOffset? RepresentativeUntil);

    private async Task<bool> AuthorityCurrentAsync(
        ModerationNoticeStatusRequest request, ModerationHostBinding host, CancellationToken token) =>
        !machine.IsMachineCaller && await identity.ResolveUserIdAsync(token) == request.SubjectId
        && !context.IsTenantFilterBypassed && context.TenantFilterTenantId == request.TenantId
        && context.ModerationTrustDomainId == host.TrustDomainId
        && await ProviderTenantMatchesAsync(request.TenantId, token)
        && await context.Users.AsNoTracking().AnyAsync(row => row.Id == request.SubjectId && !row.IsDeleted, token)
        // Deleted-subject recognition correction proof has a different purpose. A future
        // independently approved notice-status proof owner must not provision an ordinary user.
        && !await context.PrivacyErasureSagas.AsNoTracking().AnyAsync(row => row.SubjectId == request.SubjectId, token)
        && !await privacy.IsSubjectFencedAsync(request.SubjectId, token)
        && await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == request.TenantId, token)
        && await context.ModerationHostBindings.AsNoTracking().AnyAsync(row => row.Id == host.Id
            && row.TrustDomainId == host.TrustDomainId && row.SolutionId == host.SolutionId
            && row.InstanceId == host.InstanceId, token);

    private async Task<bool> ProviderTenantMatchesAsync(Guid tenantId, CancellationToken token)
    {
        if (context.Database.ProviderName != RelationalNamedLock.PostgreSqlProvider) return true;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT current_setting('app.current_tenant_id', true)";
        return await command.ExecuteScalarAsync(token) is string value
            && Guid.TryParse(value, out var current) && current == tenantId;
    }

    private sealed class StatusLease(
        ModerationNoticeStatusRepository owner, IDbContextTransaction transaction, bool ownsTransaction,
        ModerationNoticeStatusRequest request, ModerationHostBinding host,
        DateTimeOffset acquiredAt, QualifiedStatus qualified,
        EventAuthorityProviderOperations.AuthorityPlan? representativePlan) : IModerationNoticeStatusLease
    {
        private bool _disposed;
        public ModerationNoticeStatusEntry Entry { get; } = qualified.Entry;

        public async Task<ModerationNoticeReadOutcome> RevalidateAsync(CancellationToken cancellationToken)
        {
            if (_disposed || !FrameCurrent()
                || !await owner.AuthorityCurrentAsync(request, host, cancellationToken))
                return ModerationNoticeReadOutcome.NotFound;
            var current = await owner.ReadOwnedAsync(request, host, representativePlan, cancellationToken);
            if (current is null || current.Entry.Effect.Revision != Entry.Effect.Revision
                || current.Entry.Proof != Entry.Proof || current.Entry.Notification?.Id != Entry.Notification?.Id
                || current.Entry.Notification?.NotifiedAt != Entry.Notification?.NotifiedAt
                || current.Entry.Notification?.RemedyUntil != Entry.Notification?.RemedyUntil
                || current.Entry.OriginalContestationCalendarMonths != Entry.OriginalContestationCalendarMonths
                || current.Entry.OriginalRetainUntil != Entry.OriginalRetainUntil
                || !await owner.AuthorityCurrentAsync(request, host, cancellationToken))
                return ModerationNoticeReadOutcome.NotFound;
            // A clock crossing R after the last database read must also suppress prepared dates.
            var now = owner.Clock.GetUtcNow();
            return FrameCurrent()
                && (qualified.RepresentativeUntil is null || now < qualified.RepresentativeUntil)
                && (current.RepresentativeUntil is null || now < current.RepresentativeUntil)
                && (Entry.Proof is not (ModerationNoticeNotificationProof.Proven
                     or ModerationNoticeNotificationProof.Unproven)
                || now < Entry.OriginalRetainUntil)
                ? ModerationNoticeReadOutcome.Allowed : ModerationNoticeReadOutcome.NotFound;
        }

        private bool FrameCurrent() =>
            ReferenceEquals(owner.Context.Database.CurrentTransaction, transaction)
            && transaction.GetDbTransaction().Connection == owner.Context.Database.GetDbConnection()
            && !owner.Context.ChangeTracker.HasChanges() && !owner.Context.IsTenantFilterBypassed
            && owner.Context.TenantFilterTenantId == request.TenantId
            && owner.Context.ModerationTrustDomainId == host.TrustDomainId
            && owner.Clock.GetUtcNow() >= acquiredAt;

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            owner.Context.ModerationTrustDomainId = null;
            if (ownsTransaction) await transaction.DisposeAsync();
        }
    }
}
