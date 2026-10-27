using System.Collections.Immutable;
using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// The dedicated portal read owner. Notice identity is not permission: current canonical human,
/// original host/tenant/subject ancestry, retained safe disclosure and independent reviewer
/// qualification are acquired on the actual primary transaction and retained through output.
/// </summary>
public sealed partial class ModerationNoticeReadRepository(
    ExploreDbContext context, IAdminContext identity, IMachinePrincipalAccessor machine,
    ModerationNoticeRecipientRepository recipients, ModerationNativeActivationQualification activation,
    TimeProvider clock, IPrivacyIdentityFenceAuthority privacy)
    : IModerationNoticeReadRepository, IModerationNoticeNotificationRepository
{
    private ExploreDbContext Context => context;
    private TimeProvider Clock => clock;

    public Task<T> ExecuteSerializedAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        privacy.ExecuteSerializedAsync(operation, cancellationToken);

    public async Task<ModerationNoticeReadAcquisition> AcquireAsync(
        ModerationNoticeReadRequest request, CancellationToken cancellationToken)
    {
        if (request.Id is not null)
            return await AcquireWindowAsync(request, null, null, cancellationToken);
        var selected = new List<Guid>();
        var scan = request;
        Guid? upperBound = null;
        while (selected.Count < request.Limit)
        {
            var window = await AcquireWindowAsync(scan, null, upperBound, cancellationToken);
            if (window.Outcome != ModerationNoticeReadOutcome.Allowed || window.Lease is not ReadLease lease)
                return window;
            await using (lease)
            {
                selected.AddRange(lease.Entries.Select(row => row.Notice.Id).Take(request.Limit - selected.Count));
                upperBound ??= lease.UpperBound;
                if (lease.LastScannedId is not { } last || last == upperBound)
                    break;
                scan = request with { AfterId = last, Limit = request.Limit - selected.Count };
            }
        }
        // Discovery windows release all locks. Only the selected visible identifiers are
        // reacquired together for final representation; stale candidates confer no authority.
        return await AcquireWindowAsync(request, selected.ToArray(), upperBound, cancellationToken);
    }

    private async Task<ModerationNoticeReadAcquisition> AcquireWindowAsync(
        ModerationNoticeReadRequest request, Guid[]? selectedIds, Guid? upperBound, CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || request.RecipientId == Guid.Empty
            || request.Id == Guid.Empty || request.AfterId == Guid.Empty
            || request.Limit is < 1 or > 50 || request.Id is not null && request.AfterId is not null)
            return new(ModerationNoticeReadOutcome.Invalid);
        if (machine.IsMachineCaller || await identity.ResolveUserIdAsync(cancellationToken) != request.RecipientId)
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
        ReadLease? lease = null;
        try
        {
            if (!await ProviderTenantMatchesAsync(request.TenantId, cancellationToken))
                return new(ModerationNoticeReadOutcome.Unavailable);
            var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (host is null) return new(ModerationNoticeReadOutcome.NotFound);
            context.ModerationTrustDomainId = host.TrustDomainId;
            var observedAt = clock.GetUtcNow();
            var query = context.ModerationNotices.AsNoTracking().Where(row =>
                row.TrustDomainId == host.TrustDomainId
                && (row.TenantId == null || row.TenantId == request.TenantId)
                && row.RecipientSubjectKind == (int)ModerationSubjectKind.Human
                && row.RecipientSubjectId == request.RecipientId
                && context.ModerationNoticeSources.Any(source =>
                    source.EffectId == row.EffectId && source.RetainUntil > observedAt));
            if (request.Id is { } id) query = query.Where(row => row.Id == id);
            if (request.AfterId is { } after) query = query.Where(row => row.Id.CompareTo(after) > 0);
            if (selectedIds is not null) query = query.Where(row => selectedIds.Contains(row.Id));
            upperBound ??= await query.OrderByDescending(row => row.Id).Select(row => (Guid?)row.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (upperBound is { } ceiling) query = query.Where(row => row.Id.CompareTo(ceiling) <= 0);
            var discovered = await query.OrderBy(row => row.Id).Take(request.Limit).ToListAsync(cancellationToken);

            // Match the native writer order: all humans, logins, roles and memberships precede
            // host/tenant/qualification epoch and subordinate case/disclosure sources.
            var effectIds = discovered.Select(row => row.EffectId).ToArray();
            var decisionIds = await context.ModerationNoticeSources.AsNoTracking()
                .Where(row => effectIds.Contains(row.EffectId)).Select(row => row.DecisionId).ToArrayAsync(cancellationToken);
            var caseIds = await context.ModerationDecisions.AsNoTracking()
                .Where(row => decisionIds.Contains(row.Id)).Select(row => row.CaseId).ToArrayAsync(cancellationToken);
            var reviewers = await context.ModerationReviewerAssignments.AsNoTracking()
                .Where(row => caseIds.Contains(row.CaseId) && row.WithdrawnAt == null).ToListAsync(cancellationToken);
            var humans = reviewers.SelectMany(row => new Guid?[]
                { row.ReviewerHumanId, row.ControllerHumanId, row.QualifiedByHumanId, row.AssessedByHumanId })
                .Append(request.RecipientId).Where(id => id.HasValue).Select(id => id!.Value)
                .Distinct().Order().ToArray();
            var representativePlans = await DiscoverRepresentativePlansAsync(
                request, host, discovered.Select(row => row.Id).ToArray(), cancellationToken);
            var representativeRoles = representativePlans.Values.SelectMany(plan => plan.Assignments)
                .Select(row => row.RoleId).Distinct().Order().ToArray();
            await FenceHumansAsync(humans, request.TenantId, representativeRoles, cancellationToken);
            await EventAuthoritySnapshotService.AcquirePermissionSourcesAsync(context, representativeRoles, cancellationToken);
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, host.Id, cancellationToken);
            foreach (var actor in representativePlans.Values.SelectMany(plan => plan.Parents)
                .SelectMany(row => new Guid?[] { row.ActorId, row.OrganizerActorId })
                .Where(id => id.HasValue).Select(id => id!.Value).Distinct().Order())
                await RelationalEntityRowFence.AcquireGlobalAsync<Actor>(context, actor, cancellationToken);
            await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, request.TenantId, cancellationToken);
            var currentHost = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (currentHost is null || currentHost.Id != host.Id
                || currentHost.TrustDomainId != host.TrustDomainId || currentHost.SolutionId != host.SolutionId
                || currentHost.InstanceId != host.InstanceId
                || !await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == request.TenantId, cancellationToken))
                return new(ModerationNoticeReadOutcome.NotFound);
            var epoch = await context.ModerationQualificationEpochs.AsNoTracking().SingleOrDefaultAsync(
                row => row.HostBindingId == host.Id && row.TenantId == request.TenantId, cancellationToken);
            if (epoch is null) return new(ModerationNoticeReadOutcome.NotFound);
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(context, epoch.Id, cancellationToken);
            var acquiredAt = clock.GetUtcNow();
            if (!await RecipientCurrentAsync(request, cancellationToken))
                return new(ModerationNoticeReadOutcome.NotFound);

            var entries = ImmutableArray.CreateBuilder<QualifiedEntry>();
            foreach (var row in discovered)
            {
                var entry = await ReadQualifiedAsync(row.Id, request, host, acquiredAt, representativePlans, cancellationToken);
                if (entry is not null) entries.Add(entry);
            }
            if (request.Id is not null && entries.Count != 1)
                return new(ModerationNoticeReadOutcome.NotFound);
            lease = new ReadLease(this, transaction, ownsTransaction, request, host, acquiredAt, entries.ToImmutable(),
                discovered.LastOrDefault()?.Id, upperBound, representativePlans);
            if (await lease.RevalidateAsync(cancellationToken) != ModerationNoticeReadOutcome.Allowed)
                return new(ModerationNoticeReadOutcome.NotFound);
            var result = new ModerationNoticeReadAcquisition(ModerationNoticeReadOutcome.Allowed, lease);
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

    private async Task<QualifiedEntry?> ReadQualifiedAsync(
        Guid noticeId, ModerationNoticeReadRequest request, ModerationHostBinding host,
        DateTimeOffset observedAt,
        IReadOnlyDictionary<Guid, EventAuthorityProviderOperations.AuthorityPlan> representativePlans, CancellationToken token)
    {
        var notice = await context.ModerationNotices.AsNoTracking().SingleOrDefaultAsync(row =>
            row.Id == noticeId && row.RecipientSubjectKind == (int)ModerationSubjectKind.Human
            && row.RecipientSubjectId == request.RecipientId
            && (row.TenantId == null || row.TenantId == request.TenantId), token);
        if (notice is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationNotice>(context, notice.Id, token);
        await using var sourceFence = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.notice:{notice.EffectId:D}", token);
        var source = await context.ModerationNoticeSources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EffectId == notice.EffectId, token);
        var effect = await context.ModerationEffectIntents.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == notice.EffectId, token);
        if (source is null || effect is null || !source.Matches(effect)
            || source.TrustDomainId != host.TrustDomainId || notice.TrustDomainId != host.TrustDomainId
            || source.TenantId != notice.TenantId || source.CapturedAt != notice.CreatedAt)
            return null;
        ModerationNoticeCaptureSnapshot? capture;
        try { capture = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson); }
        catch (JsonException) { return null; }
        if (capture is null || !capture.Matches(effect) || capture.HasNoSubstantiveWork
            || capture.Notice is not { } snapshot || capture.HostBindingId != host.Id
            || capture.HostSolutionId != host.SolutionId || capture.HostInstanceId != host.InstanceId
            || capture.WriterTenantId != request.TenantId || observedAt < source.CapturedAt
            || snapshot.RetainUntil.Offset != TimeSpan.Zero || source.RetainUntil != snapshot.RetainUntil
            || snapshot.RetainUntil <= source.CapturedAt || snapshot.ContestationCalendarMonths is < 0 or > 120
            || snapshot.RetainUntil == DateTimeOffset.MaxValue || clock.GetUtcNow() >= snapshot.RetainUntil)
            return null;
        var notification = await context.Set<ModerationNoticeNotificationReceipt>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.EffectId == source.EffectId, token);
        if (notification is not null)
        {
            if (!notification.Matches(source, request.TenantId, snapshot.ContestationCalendarMonths, snapshot.RetainUntil)
                || notification.NotifiedAt > clock.GetUtcNow())
                return null;
        }
        else if (!ModerationNoticeRetention.CanNotifyAt(
            clock.GetUtcNow(), snapshot.ContestationCalendarMonths, snapshot.RetainUntil))
            return null;

        var instance = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId);
        if (notice.Audience == ModerationNoticeAudience.Subject
            && (capture.Decision.SubjectKind != ModerationSubjectKind.Human
                || capture.Decision.SubjectId != request.RecipientId
                || capture.Decision.SubjectOrigin != ModerationNoticeScope.From(instance)))
            return null;
        var representativeUntil = snapshot.RetainUntil;
        if (notice.Audience == ModerationNoticeAudience.ContentRepresentative)
        {
            var tenant = AuthorityScope.Tenant(instance, request.TenantId);
            var proof = capture.ContentRepresentatives.SingleOrDefault(row => row.HumanId == request.RecipientId);
            if (proof is null || capture.Decision.SubjectKind != ModerationSubjectKind.Content
                || capture.Decision.SubjectOrigin != ModerationNoticeScope.From(tenant)
                || capture.Decision.Scope.Kind != ModerationScopeKind.Resource
                || capture.Decision.Scope.Parent != ModerationNoticeScope.From(tenant)
                || !source.HasContentRepresentative(request.RecipientId))
                return null;
            if (!representativePlans.TryGetValue(noticeId, out var plan)) return null;
            DateTimeOffset? current;
            try
            {
                current = await ModerationNoticeContentCustody.AcquireAsync(context, privacy,
                    SubjectReference.Create(ModerationSubjectKind.Content, capture.Decision.SubjectId, tenant),
                    tenant, [proof], [request.RecipientId], clock.GetUtcNow(), snapshot.RetainUntil, token, plan);
            }
            catch (DbUpdateConcurrencyException) { return null; }
            if (current is null) return null;
            representativeUntil = current.Value;
        }

        // A later sanction reversal, expired enforcement interval or retired publication does not
        // revoke this original explanation. Explicit safe-disclosure revocation does.
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationNoticeDisclosureApproval>(
            context, snapshot.ApprovalId, token);
        var approval = await context.ModerationNoticeDisclosureApprovals.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == snapshot.ApprovalId, token);
        if (approval is null) return null;
        approval.ValidatePersistedState();
        if (approval.RevokedAt is not null || approval.Revision != snapshot.ApprovalRevision
            || approval.HostBindingId != host.Id || approval.TenantId != request.TenantId
            || approval.TrustDomainId != host.TrustDomainId || approval.ApprovedAt > source.CapturedAt
            || approval.EffectId != source.EffectId || approval.DecisionId != source.DecisionId
            || approval.DecisionRevision != source.DecisionRevision || approval.CaseId != capture.Decision.CaseId
            || approval.PolicyId != snapshot.PolicyId || approval.PolicyRevision != snapshot.PolicyRevision
            || approval.RetainUntil != snapshot.RetainUntil
            || approval.PublicationId != snapshot.PublicationId || approval.ProfileId != snapshot.ProfileId
            || approval.ContributionId != snapshot.ContributionId
            || approval.RuleMeaning != snapshot.RuleMeaning.Value || approval.ReasonMeaning != snapshot.ReasonMeaning.Value
            || !approval.Intent.Facts.SequenceEqual(snapshot.Facts)
            || approval.RemainingActions != snapshot.RemainingActions.Value || approval.Recourse != snapshot.Recourse.Value
            || approval.Automation != snapshot.Automation.Value || approval.Retention != snapshot.Retention.Value)
            return null;
        if (!approval.Intent.ContentRepresentatives.SequenceEqual(capture.ContentRepresentatives))
            return null;

        var validUntil = representativeUntil;
        if (notice.Audience == ModerationNoticeAudience.Reviewer)
        {
            if (!capture.Reviewers.Any(row => row.ReviewerHumanId == request.RecipientId))
                return null;
            var decision = await context.ModerationDecisions.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == source.DecisionId, token);
            var allegation = await context.ModerationCases.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == capture.Decision.CaseId, token);
            if (decision is null || allegation is null) return null;
            decision.ValidatePersistedState();
            allegation.ValidatePersistedState();
            if (decision.CaseId != allegation.Id || ModerationNoticeScope.From(decision.Scope) != snapshot.Scope
                || decision.SubjectId != snapshot.SubjectId || decision.Subject.Kind != snapshot.SubjectKind
                || ModerationNoticeScope.From(decision.Subject.Origin) != snapshot.SubjectOrigin)
                return null;
            var now = clock.GetUtcNow();
            var procedural = await activation.AcquireQualificationPolicyAsync(host.Id, allegation, now, token);
            if (procedural is null) return null;
            var current = procedural.Current;
            var adopted = procedural.Approval;
            var qualification = new ModerationNoticeReviewerQualification(allegation.Id, decision.Scope,
                new(adopted.ApprovedSourceId, adopted.ApprovedSourceRevision, adopted.ApprovedWriterGeneration),
                new(current.SourceId, current.SourceRevision, current.WriterGeneration),
                current.MaximumReviewerLifetime, now, procedural.ValidUntil);
            var qualified = await recipients.ReadReviewersAsync(decision, qualification, now, token);
            var reviewer = qualified.SingleOrDefault(row => row.ReviewerHumanId == request.RecipientId);
            if (reviewer is null || !reviewer.IsQualifiedAt(clock.GetUtcNow())
                || clock.GetUtcNow() >= procedural.ValidUntil)
                return null;
            validUntil = new[] { validUntil, procedural.ValidUntil, reviewer.QualifiedUntil!.Value }.Min();
        }
        else if (notice.Audience is not (ModerationNoticeAudience.Subject or ModerationNoticeAudience.ContentRepresentative))
            return null;
        return clock.GetUtcNow() >= observedAt && clock.GetUtcNow() < validUntil
            ? new(new(notice, source, notification), validUntil) : null;
    }

    private Task<IReadOnlyDictionary<Guid, EventAuthorityProviderOperations.AuthorityPlan>> DiscoverRepresentativePlansAsync(
        ModerationNoticeReadRequest request, ModerationHostBinding host, Guid[] noticeIds, CancellationToken token) =>
        EventAuthorityProviderOperations.ReadWithoutRetainingLocksAsync<IReadOnlyDictionary<Guid, EventAuthorityProviderOperations.AuthorityPlan>>(
            context, async planning =>
            {
                var candidates = await planning.ModerationNotices.AsNoTracking().Where(row => noticeIds.Contains(row.Id)
                    && row.TrustDomainId == host.TrustDomainId && row.TenantId == request.TenantId
                    && row.Audience == ModerationNoticeAudience.ContentRepresentative
                    && row.RecipientSubjectKind == (int)ModerationSubjectKind.Human && row.RecipientSubjectId == request.RecipientId)
                    .Join(planning.ModerationNoticeSources.AsNoTracking(), notice => notice.EffectId, source => source.EffectId,
                        (notice, source) => new { notice.Id, source.SnapshotJson }).ToArrayAsync(token);
                var plans = ImmutableDictionary.CreateBuilder<Guid, EventAuthorityProviderOperations.AuthorityPlan>();
                foreach (var candidate in candidates)
                {
                    ModerationNoticeCaptureSnapshot? capture;
                    try { capture = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(candidate.SnapshotJson); }
                    catch (JsonException) { continue; }
                    if (capture?.Decision is not { SubjectKind: ModerationSubjectKind.Content } decision) continue;
                    // Original coordinates propose anchors only; ReadQualifiedAsync reacquires every disclosure proof.
                    plans.Add(candidate.Id, new(
                        await EventAuthorityProviderOperations.ReadOwnershipAsync(planning, request.TenantId,
                            [decision.SubjectId], token),
                        await EventAuthorityProviderOperations.ReadAssignmentsAsync(planning, request.TenantId,
                            [request.RecipientId], [decision.SubjectId], token)));
                }
                return plans.ToImmutable();
            }, token);

    private async Task FenceHumansAsync(Guid[] humans, Guid tenantId, int[] representativeRoles, CancellationToken token)
    {
        foreach (var id in humans)
            await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, id, token);
        var logins = await context.UserExternalLogins.AsNoTracking().Where(row => humans.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => row.Id).ToListAsync(token);
        foreach (var id in logins)
            await RelationalEntityRowFence.AcquireGlobalAsync<UserExternalLogin>(context, id, token);
        var platform = await context.PlatformUserRoles.AsNoTracking().Where(row => humans.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToListAsync(token);
        foreach (var grant in platform)
            await RelationalEntityRowFence.AcquireGlobalAsync<PlatformUserRole>(context, grant.Id, token);
        var memberships = await context.TenantUsers.AsNoTracking()
            .Where(row => row.TenantId == tenantId && humans.Contains(row.UserId)).OrderBy(row => row.Id)
            .Select(row => row.Id).ToListAsync(token);
        foreach (var id in memberships)
            await RelationalEntityRowFence.AcquireAsync<TenantUser>(context, tenantId, row => row.Id, id, token);
        var grants = await context.TenantUserRoleGrants.AsNoTracking().Where(row => memberships.Contains(row.TenantUserId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToListAsync(token);
        foreach (var grant in grants)
            await RelationalEntityRowFence.AcquireAsync<TenantUserRoleGrant>(context, tenantId, row => row.Id, grant.Id, token);
        foreach (var id in platform.Select(row => row.RoleId).Concat(grants.Select(row => row.RoleId))
            .Concat(representativeRoles).Distinct().Order())
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [id], token);
    }

    private async Task<bool> RecipientCurrentAsync(ModerationNoticeReadRequest request, CancellationToken token) =>
        !machine.IsMachineCaller && await identity.ResolveUserIdAsync(token) == request.RecipientId
        && await context.Users.AsNoTracking().AnyAsync(row => row.Id == request.RecipientId && !row.IsDeleted, token)
        && !await context.PrivacyErasureSagas.AsNoTracking().AnyAsync(row => row.SubjectId == request.RecipientId, token)
        && !await privacy.IsSubjectFencedAsync(request.RecipientId, token);

    private async Task<bool> ProviderTenantMatchesAsync(Guid tenantId, CancellationToken token)
    {
        if (context.Database.ProviderName != RelationalNamedLock.PostgreSqlProvider) return true;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT current_setting('app.current_tenant_id', true)";
        return await command.ExecuteScalarAsync(token) is string value
            && Guid.TryParse(value, out var current) && current == tenantId;
    }

    private sealed record QualifiedEntry(ModerationNoticeReadEntry Entry, DateTimeOffset ValidUntil);

    private sealed class ReadLease(
        ModerationNoticeReadRepository owner, IDbContextTransaction transaction, bool ownsTransaction,
        ModerationNoticeReadRequest request, ModerationHostBinding host,
        DateTimeOffset acquiredAt, ImmutableArray<QualifiedEntry> entries,
        Guid? lastScannedId, Guid? upperBound,
        IReadOnlyDictionary<Guid, EventAuthorityProviderOperations.AuthorityPlan> representativePlans) : IModerationNoticeReadLease
    {
        private bool _disposed;
        public ImmutableArray<ModerationNoticeReadEntry> Entries { get; } =
            entries.Select(row => row.Entry).ToImmutableArray();
        public Guid? LastScannedId { get; } = lastScannedId;
        public Guid? UpperBound { get; } = upperBound;
        public IReadOnlyDictionary<Guid, EventAuthorityProviderOperations.AuthorityPlan> RepresentativePlans { get; } = representativePlans;

        public async Task<ModerationNoticeReadOutcome> RevalidateAsync(CancellationToken cancellationToken)
        {
            if (_disposed || !ReferenceEquals(owner.Context.Database.CurrentTransaction, transaction)
                || transaction.GetDbTransaction().Connection != owner.Context.Database.GetDbConnection()
                || owner.Context.IsTenantFilterBypassed || owner.Context.TenantFilterTenantId != request.TenantId
                || owner.Context.ModerationTrustDomainId != host.TrustDomainId
                || owner.Context.ChangeTracker.HasChanges() || owner.Clock.GetUtcNow() < acquiredAt
                || !await owner.ProviderTenantMatchesAsync(request.TenantId, cancellationToken)
                || !await owner.RecipientCurrentAsync(request, cancellationToken))
                return ModerationNoticeReadOutcome.NotFound;
            var deadlines = new List<DateTimeOffset>();
            foreach (var entry in Entries)
            {
                var current = await owner.ReadQualifiedAsync(entry.Notice.Id, request, host, acquiredAt, RepresentativePlans, cancellationToken);
                if (current is null || current.Entry.Source.SnapshotHash != entry.Source.SnapshotHash
                    || current.Entry.Notice.Audience != entry.Notice.Audience
                    || current.Entry.Notification?.Id != entry.Notification?.Id)
                    return ModerationNoticeReadOutcome.NotFound;
                deadlines.Add(current.ValidUntil);
            }
            if (!await owner.RecipientCurrentAsync(request, cancellationToken))
                return ModerationNoticeReadOutcome.NotFound;
            var now = owner.Clock.GetUtcNow();
            return now >= acquiredAt && deadlines.All(until => now < until)
                && Entries.All(entry => entry.Notification is not null || CanNotify(entry.Source, now))
                && ReferenceEquals(owner.Context.Database.CurrentTransaction, transaction)
                && !owner.Context.ChangeTracker.HasChanges()
                && !owner.Context.IsTenantFilterBypassed && owner.Context.TenantFilterTenantId == request.TenantId
                && owner.Context.ModerationTrustDomainId == host.TrustDomainId
                ? ModerationNoticeReadOutcome.Allowed : ModerationNoticeReadOutcome.NotFound;
        }

        private static bool CanNotify(ModerationNoticeSource source, DateTimeOffset now)
        {
            var snapshot = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson)!.Notice!;
            return ModerationNoticeRetention.CanNotifyAt(now, snapshot.ContestationCalendarMonths, snapshot.RetainUntil);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            owner.Context.ModerationTrustDomainId = null;
            if (ownsTransaction) await transaction.DisposeAsync();
        }
    }
}
