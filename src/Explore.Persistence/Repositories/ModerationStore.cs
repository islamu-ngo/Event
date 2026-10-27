using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Models.InternalEvents;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>Owns the primary-store moderation transaction, exact receipt and qualified entity/effect persistence.</summary>
public sealed partial class ModerationStore(
    ExploreDbContext context, IModerationAuthorityContext authority, TimeProvider clock,
    IPrivacyIdentityFenceAuthority privacy, IModerationNoticeCapture noticeCapture) : IModerationStore
{
    public async Task<ModerationOperationResult> ExecuteAsync(
        ModerationOperation operation,
        Func<IModerationSession, CancellationToken, Task<ModerationOperationResult>> execute,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(execute);
        if (authority is not IModerationAuthorityParticipant participant || !context.Database.IsRelational()
            || context.Database.CurrentTransaction is not null || context.IsTenantFilterBypassed
            || context.ModerationTrustDomainId is not null || context.ChangeTracker.Entries().Any()
            || context.TenantFilterTenantId == Guid.Empty)
        {
            return ModerationOperationResult.Unavailable();
        }

        var tenantId = context.TenantFilterTenantId;
        var targetTenant = Ancestor(operation.Target, ModerationScopeKind.Tenant);
        if (targetTenant is not null && targetTenant != tenantId)
        {
            return ModerationOperationResult.Unavailable();
        }

        var digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1,
            Kind = (int)operation.Kind,
            SubjectKind = (int)operation.Subject.Kind,
            operation.Subject.Id,
            Origin = Coordinates(operation.Subject.Origin),
            Target = Coordinates(operation.Target),
            operation.OperationId,
            operation.EffectId,
            operation.DecisionId,
            operation.ExpectedRevision,
            operation.ExpectedPolicyRevision,
            operation.ReferenceId,
            operation.Proposal,
            operation.ExpectedSanctionRevision,
            operation.SupersededSanctionId,
            SupersededScope = operation.SupersededScope is { } supersededScope ? Coordinates(supersededScope) : null,
            Capability = operation.Capability is { } capability ? (int?)capability : null
        })));
        var session = new Session(context, operation, digest, clock, noticeCapture);
        try
        {
            async Task<ModerationOperationResult> ApplyAsync(CancellationToken token)
            {
                session = new(context, operation, digest, clock, noticeCapture);
                context.ChangeTracker.Clear();
                context.ModerationTrustDomainId = null;
                if (context.TenantFilterTenantId != tenantId || context.IsTenantFilterBypassed
                    || !await ProviderTenantMatchesAsync(tenantId, token))
                {
                    throw new AbortOperation(ModerationOperationResult.Unavailable());
                }

                var resolution = await participant.AcquireAsync(session, operation, token);
                if (resolution.Outcome != ModerationOperationOutcome.Known || resolution.Grant is not { } grant)
                {
                    throw new AbortOperation(resolution.Outcome == ModerationOperationOutcome.Denied
                        ? ModerationOperationResult.Denied() : ModerationOperationResult.Unavailable());
                }

                if (grant.Subject != operation.Subject || grant.Purpose != operation.Kind
                    || !grant.Scope.Contains(operation.Target))
                {
                    throw new AbortOperation(ModerationOperationResult.Denied());
                }

                session.Grant = grant;
                context.ModerationTrustDomainId = grant.Scope.TrustDomainId;
                if (operation.Kind != ModerationOperationKind.Evaluate)
                {
                    await using var operationFence = await RelationalNamedLock.AcquireTransactionAsync(
                        context, $"moderation.operation:{operation.OperationId:D}", token);
                    var receipt = await context.ModerationOperationReceipts.AsNoTracking()
                        .SingleOrDefaultAsync(entity => entity.Id == operation.OperationId, token);
                    if (receipt is not null)
                    {
                        return await CommittedResultAsync(receipt, operation, digest, grant, session, token);
                    }
                }

                session.Saved = false;
                session.CaptureValidUntil = null;
                session.NoticeRetention = null;
                session.Receipt = null;
                var result = await execute(session, token);
                if (result.Outcome != ModerationOperationOutcome.Known)
                {
                    throw new AbortOperation(result);
                }
                if (context.TenantFilterTenantId != tenantId || context.IsTenantFilterBypassed
                    || !await ProviderTenantMatchesAsync(tenantId, token))
                {
                    throw new AbortOperation(ModerationOperationResult.Unavailable());
                }

                if (operation.Kind != ModerationOperationKind.Evaluate)
                {
                    if (!session.Saved || result.DecisionId is not { } decisionId || result.Revision is not { } revision
                        || session.Grant != grant || session.Receipt is not { } staged
                        || staged.DecisionId != decisionId || staged.SanctionId != result.SanctionId
                        || staged.ResultRevision != revision)
                    {
                        throw new AbortOperation(ModerationOperationResult.Unavailable());
                    }

                    await context.SaveChangesAsync(token);
                    await context.FlushDisclosureAsync(token);
                    if (!session.CaptureIsCurrentAt(clock.GetUtcNow())
                        || context.TenantFilterTenantId != tenantId || context.IsTenantFilterBypassed
                        || !await ProviderTenantMatchesAsync(tenantId, token))
                        throw new AbortOperation(ModerationOperationResult.Unavailable());
                }

                var current = await authority.ResolveAsync(session, operation, token);
                if (current.Outcome != ModerationOperationOutcome.Known || current.Grant != grant
                    || !session.CaptureIsCurrentAt(clock.GetUtcNow()))
                    throw new AbortOperation(ModerationOperationResult.Unavailable());
                return result;
            }

            async Task<bool> VerifyCommittedAsync(CancellationToken token)
            {
                session = new(context, operation, digest, clock, noticeCapture);
                context.ChangeTracker.Clear();
                context.ModerationTrustDomainId = null;
                await using var verification = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                if (operation.Kind == ModerationOperationKind.Evaluate
                    || context.TenantFilterTenantId != tenantId || context.IsTenantFilterBypassed
                    || !await ProviderTenantMatchesAsync(tenantId, token))
                {
                    return false;
                }

                var resolution = await participant.AcquireAsync(session, operation, token);
                if (resolution.Grant is not { } grant || resolution.Outcome != ModerationOperationOutcome.Known
                    || grant.Subject != operation.Subject || grant.Purpose != operation.Kind
                    || !grant.Scope.Contains(operation.Target))
                {
                    return false;
                }

                context.ModerationTrustDomainId = grant.Scope.TrustDomainId;
                var receipt = await context.ModerationOperationReceipts.AsNoTracking()
                    .SingleOrDefaultAsync(entity => entity.Id == operation.OperationId, token);
                return receipt is not null
                    && (await CommittedResultAsync(receipt, operation, digest, grant, session, token)).Outcome
                        == ModerationOperationOutcome.Known;
            }

            if (operation.Subject.Kind == ModerationSubjectKind.Content
                    && operation.Kind != ModerationOperationKind.Evaluate
                || operation.Kind is ModerationOperationKind.RecordResponse
                    or ModerationOperationKind.AcceptFinding or ModerationOperationKind.Approve
                    or ModerationOperationKind.Activate or ModerationOperationKind.Supersede
                    or ModerationOperationKind.Reconcile or ModerationOperationKind.Revoke)
            {
                Func<CancellationToken, Task> beforePrimaryCommit = _ => Task.CompletedTask;
                return await context.Database.CreateExecutionStrategy().ExecuteAsync(() =>
                    privacy.ExecuteEnrollmentSerializedAsync(async token =>
                    {
                        beforePrimaryCommit = _ => Task.CompletedTask;
                        if (context.Database.CurrentTransaction is not null && !context.IdentityFenceOwnsTransaction)
                            throw new AbortOperation(ModerationOperationResult.Unavailable());
                        await using IDbContextTransaction? owned = context.IdentityFenceOwnsTransaction ? null
                            : await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                        var transaction = context.Database.CurrentTransaction
                            ?? throw new AbortOperation(ModerationOperationResult.Unavailable());
                        if (transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                            throw new AbortOperation(ModerationOperationResult.Unavailable());
                        var result = await ApplyAsync(token);
                        var grant = session.Grant ?? throw new AbortOperation(ModerationOperationResult.Unavailable());
                        beforePrimaryCommit = async finalToken =>
                        {
                            if (!ReferenceEquals(context.Database.CurrentTransaction, transaction)
                                || context.TenantFilterTenantId != tenantId || context.IsTenantFilterBypassed
                                || context.ModerationTrustDomainId != grant.Scope.TrustDomainId
                                || context.ChangeTracker.HasChanges()
                                || !await ProviderTenantMatchesAsync(tenantId, finalToken))
                                throw new AbortOperation(ModerationOperationResult.Unavailable());
                            var receipt = await context.ModerationOperationReceipts.AsNoTracking()
                                .SingleOrDefaultAsync(row => row.Id == operation.OperationId, finalToken);
                            if (receipt is null)
                                throw new AbortOperation(ModerationOperationResult.Unavailable());
                            var verified = await CommittedResultAsync(receipt, operation, digest, grant, session, finalToken);
                            if (verified.Outcome != result.Outcome || verified.DecisionId != result.DecisionId
                                || verified.SanctionId != result.SanctionId || verified.Revision != result.Revision)
                                throw new AbortOperation(ModerationOperationResult.Unavailable());
                            if (session.Saved && (operation.Subject.Kind == ModerationSubjectKind.Content
                                || operation.Kind == ModerationOperationKind.Revoke))
                                await RequireContentCaptureAtCommitAsync(session, operation, finalToken);
                            var current = await authority.ResolveAsync(session, operation, finalToken);
                            if (current.Outcome != ModerationOperationOutcome.Known || current.Grant != grant
                                || !session.CaptureIsCurrentAt(clock.GetUtcNow()))
                                throw new AbortOperation(ModerationOperationResult.Unavailable());
                        };
                        await beforePrimaryCommit(token);
                        if (owned is not null) await owned.CommitAsync(token);
                        return result;
                    }, token => beforePrimaryCommit(token), cancellationToken));
            }

            return await context.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(
                operation: ApplyAsync, verifySucceeded: VerifyCommittedAsync,
                isolationLevel: IsolationLevel.Serializable,
                cancellationToken: cancellationToken);
        }
        catch (AbortOperation aborted)
        {
            return aborted.Result;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ModerationOperationResult.Conflict();
        }
        catch (DbUpdateException exception) when (RegistrationUniqueConflictClassifier.IsProviderUniqueConflict(exception))
        {
            return ModerationOperationResult.Conflict();
        }
        finally
        {
            context.ModerationTrustDomainId = null;
            context.ChangeTracker.Clear();
        }
    }

    private async Task<ModerationOperationResult> CommittedResultAsync(
        ModerationOperationReceipt receipt, ModerationOperation operation, string digest,
        ModerationAuthorityGrant grant, IModerationSession session, CancellationToken token)
    {
        var result = ReceiptResult(receipt, operation, digest, grant);
        DateTimeOffset? pendingUntil = null;
        if (result.Outcome != ModerationOperationOutcome.Known) return result;
        var effect = await context.ModerationEffectIntents.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == operation.EffectId, token);
        var observedAt = clock.GetUtcNow();
        var source = await context.ModerationNoticeSources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EffectId == operation.EffectId
                && (operation.Kind != ModerationOperationKind.Revoke || row.RetainUntil > observedAt), token);
        if (effect is null || effect.OperationId != operation.OperationId
            || effect.DecisionId != operation.DecisionId || effect.DecisionId != receipt.DecisionId
            || effect.SanctionId != receipt.SanctionId || effect.PolicyRevision != receipt.PolicyRevision
            || effect.QualificationRevision != receipt.QualificationRevision
            || receipt.ResultRevision != (operation.Kind is ModerationOperationKind.Revoke or ModerationOperationKind.Reconcile
                ? effect.SanctionRevision : effect.DecisionRevision))
            return ModerationOperationResult.Unavailable();
        if (effect.OriginalNoticeCleanupId is not null)
        {
            var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
            var prefix = $"moderation-notice:{effect.Id:N}:";
            if (source is not null || host is null || context.TenantFilterTenantId is not { } tenantId
                || !effect.HasOriginalNoticeCleanup(host.Id, tenantId, clock.GetUtcNow())
                || await context.ModerationNotices.AsNoTracking().AnyAsync(row => row.EffectId == effect.Id, token)
                || await context.ModerationNoticeNotificationReceipts.AsNoTracking().AnyAsync(row => row.EffectId == effect.Id, token)
                || await context.ModerationNoticeDisclosureApprovals.AsNoTracking().AnyAsync(row => row.EffectId == effect.Id, token)
                || await context.NotificationIntents.AsNoTracking().IgnoreQueryFilters(["SoftDelete"])
                    .AnyAsync(row => row.TenantId == tenantId && row.DeduplicationKey.StartsWith(prefix), token))
                return ModerationOperationResult.Unavailable();
        }
        else
        {
            if (operation.Kind == ModerationOperationKind.Revoke && source?.RetainUntil is { } revokeUntil
                && clock.GetUtcNow() >= revokeUntil) return ModerationOperationResult.Unavailable();
            if (source is null || !source.Matches(effect)) return ModerationOperationResult.Unavailable();
            ModerationNoticeCaptureSnapshot? captured;
            try { captured = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson); }
            catch (JsonException) { return ModerationOperationResult.Unavailable(); }
            if (captured is null || !captured.Matches(effect) || captured.WriterTenantId != context.TenantFilterTenantId)
                return ModerationOperationResult.Unavailable();
            if (captured.HasNoSubstantiveWork && source.TenantId != captured.WriterTenantId)
                return ModerationOperationResult.Unavailable();
            if (captured.PendingCorrection is { } pending
                && !await ModerationNoticeCorrectionAudience.PendingOccurrencesMatchAsync(context, source, pending, clock, token))
                return ModerationOperationResult.Unavailable();
            pendingUntil = captured.PendingCorrection?.RetainUntil;
            if (!captured.HasNoSubstantiveWork)
            {
                var notices = await context.ModerationNotices.AsNoTracking()
                    .Where(row => row.EffectId == effect.Id).ToListAsync(token);
                if (!notices.Any(row => row.Audience == ModerationNoticeAudience.Subject
                        && row.RecipientSubjectKind == effect.SubjectKind && row.RecipientSubjectId == effect.SubjectId)
                    || !captured.Reviewers.All(proof => notices.Any(row => row.Audience == ModerationNoticeAudience.Reviewer
                        && row.RecipientSubjectKind == (int)ModerationSubjectKind.Human
                        && row.RecipientSubjectId == proof.ReviewerHumanId))
                    || !captured.ContentRepresentatives.All(proof => notices.Any(row =>
                        row.Audience == ModerationNoticeAudience.ContentRepresentative
                        && row.RecipientSubjectKind == (int)ModerationSubjectKind.Human
                        && row.RecipientSubjectId == proof.HumanId
                        && row.TrustDomainId == source.TrustDomainId && row.TenantId == source.TenantId
                        && row.CreatedAt == source.CapturedAt)))
                    return ModerationOperationResult.Unavailable();
            }
        }
        // Replays prove the original atomic write, not today's disclosure authority or decision.
        // The native permission/deadline that allowed this replay must still hold after these reads.
        var current = await authority.ResolveAsync(session, operation, token);
        return current.Outcome == ModerationOperationOutcome.Known && current.Grant == grant
            && (pendingUntil is null || clock.GetUtcNow() < pendingUntil.Value)
            ? result : ModerationOperationResult.Unavailable();
    }

    private async Task<bool> ProviderTenantMatchesAsync(Guid? tenantId, CancellationToken token)
    {
        if (context.Database.ProviderName != RelationalNamedLock.PostgreSqlProvider)
        {
            return true;
        }

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT current_setting('app.current_tenant_id', true)";
        var value = await command.ExecuteScalarAsync(token);
        if (tenantId is null)
        {
            return value is null or DBNull || value is string { Length: 0 };
        }
        return value is string setting && Guid.TryParse(setting, out var providerTenant) && providerTenant == tenantId;
    }

    private static ModerationOperationResult ReceiptResult(
        ModerationOperationReceipt receipt, ModerationOperation operation, string digest, ModerationAuthorityGrant grant) =>
        receipt.RequestDigest == digest && receipt.TrustDomainId == operation.Target.TrustDomainId
            && receipt.ScopeKind == (int)operation.Target.Kind && receipt.ScopeId == operation.Target.Id
            && receipt.SubjectKind == (int)operation.Subject.Kind && receipt.SubjectId == operation.Subject.Id
            && receipt.PrincipalId == grant.Principal.Id && receipt.PrincipalDigest == PrincipalDigest(grant)
                ? ModerationOperationResult.Applied(receipt.DecisionId, receipt.SanctionId, receipt.ResultRevision)
                : ModerationOperationResult.Conflict();

    private static string PrincipalDigest(ModerationAuthorityGrant grant) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Kind = (int)grant.Principal.Kind,
            grant.Principal.Id,
            Origin = Coordinates(grant.Principal.Origin)
        })));

    private static Guid? Ancestor(AuthorityScope scope, ModerationScopeKind kind)
    {
        for (AuthorityScope? current = scope; current is not null; current = current.Parent)
        {
            if (current.Kind == kind) return current.Id;
        }
        return null;
    }

    private static IReadOnlyList<object> Coordinates(AuthorityScope scope)
    {
        var coordinates = new List<object>();
        for (AuthorityScope? current = scope; current is not null; current = current.Parent)
        {
            coordinates.Add(new { Kind = (int)current.Kind, current.Id });
        }
        return coordinates.AsReadOnly();
    }

    private sealed class AbortOperation(ModerationOperationResult result) : Exception
    {
        public ModerationOperationResult Result { get; } = result;
    }

    internal static bool OwnsCaptureSession(IModerationSession session, ExploreDbContext receivingContext,
        ModerationOperation operation, ModerationAuthorityGrant grant) =>
        session is Session native && native.OwnsCapture(receivingContext, operation, grant);

    private sealed class Session(
        ExploreDbContext db, ModerationOperation operation, string digest, TimeProvider clock,
        IModerationNoticeCapture noticeCapture) : IModerationSession
    {
        internal ModerationAuthorityGrant? Grant { get; set; }
        internal bool OwnsCapture(ExploreDbContext receivingContext, ModerationOperation requested,
            ModerationAuthorityGrant grant) => ReferenceEquals(db, receivingContext)
                && ReferenceEquals(operation, requested) && ReferenceEquals(Grant, grant);
        internal bool Saved { get; set; }
        internal ModerationOperationReceipt? Receipt { get; set; }
        internal DateTimeOffset? CaptureValidUntil { get; set; }
        internal ModerationNoticeCaptureSnapshot? OriginalCapture { get; set; }
        internal ModerationNoticeSource? CapturedSource { get; set; }
        internal (DateTimeOffset CapturedAt, DateTimeOffset RetainUntil, int CalendarMonths)? NoticeRetention { get; set; }

        internal bool CaptureIsCurrentAt(DateTimeOffset now) =>
            (CaptureValidUntil is null || now < CaptureValidUntil)
            && (NoticeRetention is not { } notice || now >= notice.CapturedAt
                && ModerationNoticeRetention.CanNotifyAt(now, notice.CalendarMonths, notice.RetainUntil));

        public async Task<ModerationCase?> FindCaseAsync(
            Guid caseId, SubjectReference subject, AuthorityScope scope, CancellationToken token)
        {
            // Case attribution is optional. This adjudication lookup requires the complete
            // actual subject tuple and can never turn a target-only allegation into a subject.
            var query = db.ModerationCases.Where(
                entity => entity.Id == caseId && entity.SubjectKind == subject.Kind && entity.SubjectId == subject.Id
                    && entity.SubjectOriginKind == subject.Origin.Kind
                    && entity.SubjectOriginTrustDomainId == subject.TrustDomainId
                    && entity.SubjectOriginSolutionId == Ancestor(subject.Origin, ModerationScopeKind.Solution)
                    && entity.SubjectOriginInstanceId == Ancestor(subject.Origin, ModerationScopeKind.Instance)
                    && entity.SubjectOriginTenantId == Ancestor(subject.Origin, ModerationScopeKind.Tenant)
                    && entity.SubjectOriginCommunityId == Ancestor(subject.Origin, ModerationScopeKind.Community)
                    && entity.SubjectOriginResourceId == Ancestor(subject.Origin, ModerationScopeKind.Resource)
                    && entity.SubjectOriginEventId == Ancestor(subject.Origin, ModerationScopeKind.Event)
                    && entity.ScopeTrustDomainId == scope.TrustDomainId
                    && entity.ScopeSolutionId == Ancestor(scope, ModerationScopeKind.Solution)
                    && entity.ScopeInstanceId == Ancestor(scope, ModerationScopeKind.Instance)
                    && entity.ScopeTenantId == Ancestor(scope, ModerationScopeKind.Tenant)
                    && entity.ScopeCommunityId == Ancestor(scope, ModerationScopeKind.Community)
                    && (entity.ScopeResourceId == Ancestor(scope, ModerationScopeKind.Resource)
                            && entity.ScopeEventId == Ancestor(scope, ModerationScopeKind.Event)
                            && entity.ScopeKind == scope.Kind
                        || (scope.Kind == ModerationScopeKind.Resource || scope.Kind == ModerationScopeKind.Event)
                            && scope.Parent!.Kind == ModerationScopeKind.Tenant
                            && entity.AttributionSourceId != null && entity.ScopeResourceId == null
                            && entity.ScopeEventId == null
                            && entity.ScopeKind == ModerationScopeKind.Tenant));
            if (!await query.AnyAsync(token)) return null;
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCase>(db, caseId, token);
            var allegation = await query.SingleOrDefaultAsync(token);
            allegation?.ValidatePersistedState();
            return allegation?.Subject == subject ? allegation : null;
        }

        public async Task<ModerationDecision?> FindDecisionAsync(
            Guid decisionId, SubjectReference subject, AuthorityScope scope, CancellationToken token)
        {
            var query = QualifiedSubject(db.ModerationDecisions, subject).Where(
                entity => entity.Id == decisionId && entity.SubjectKind == subject.Kind && entity.SubjectId == subject.Id
                    && entity.ScopeTrustDomainId == scope.TrustDomainId
                    && entity.ScopeSolutionId == Ancestor(scope, ModerationScopeKind.Solution)
                    && entity.ScopeInstanceId == Ancestor(scope, ModerationScopeKind.Instance)
                    && entity.ScopeTenantId == Ancestor(scope, ModerationScopeKind.Tenant)
                    && entity.ScopeCommunityId == Ancestor(scope, ModerationScopeKind.Community)
                    && entity.ScopeResourceId == Ancestor(scope, ModerationScopeKind.Resource)
                    && entity.ScopeEventId == Ancestor(scope, ModerationScopeKind.Event)
                    && entity.ScopeKind == scope.Kind);
            if (!await query.AnyAsync(token)) return null;
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationDecision>(db, decisionId, token);
            var decision = await query.SingleOrDefaultAsync(token);
            decision?.ValidatePersistedState();
            return decision?.Subject == subject ? decision : null;
        }

        public async Task<ModerationFinding?> FindFindingAsync(
            Guid findingId, SubjectReference subject, AuthorityScope scope, CancellationToken token)
        {
            var query = QualifiedSubject(db.ModerationFindings, subject).Where(
                entity => entity.Id == findingId && entity.SubjectKind == subject.Kind && entity.SubjectId == subject.Id
                    && entity.ScopeTrustDomainId == scope.TrustDomainId
                    && entity.ScopeSolutionId == Ancestor(scope, ModerationScopeKind.Solution)
                    && entity.ScopeInstanceId == Ancestor(scope, ModerationScopeKind.Instance)
                    && entity.ScopeTenantId == Ancestor(scope, ModerationScopeKind.Tenant)
                    && entity.ScopeCommunityId == Ancestor(scope, ModerationScopeKind.Community)
                    && entity.ScopeResourceId == Ancestor(scope, ModerationScopeKind.Resource)
                    && entity.ScopeEventId == Ancestor(scope, ModerationScopeKind.Event)
                    && entity.ScopeKind == scope.Kind);
            if (!await query.AnyAsync(token)) return null;
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationFinding>(db, findingId, token);
            var finding = await query.SingleOrDefaultAsync(token);
            finding?.ValidatePersistedState();
            return finding?.Subject == subject ? finding : null;
        }

        public async Task<ModerationSanction?> FindSanctionAsync(
            Guid sanctionId, SubjectReference subject, AuthorityScope scope, CancellationToken token)
        {
            var query = QualifiedSubject(db.ModerationSanctions, subject).Where(
                entity => entity.Id == sanctionId && entity.SubjectKind == subject.Kind && entity.SubjectId == subject.Id
                    && entity.ScopeTrustDomainId == scope.TrustDomainId
                    && entity.ScopeSolutionId == Ancestor(scope, ModerationScopeKind.Solution)
                    && entity.ScopeInstanceId == Ancestor(scope, ModerationScopeKind.Instance)
                    && entity.ScopeTenantId == Ancestor(scope, ModerationScopeKind.Tenant)
                    && entity.ScopeCommunityId == Ancestor(scope, ModerationScopeKind.Community)
                    && entity.ScopeResourceId == Ancestor(scope, ModerationScopeKind.Resource)
                    && entity.ScopeEventId == Ancestor(scope, ModerationScopeKind.Event)
                    && entity.ScopeKind == scope.Kind);
            if (!await query.AnyAsync(token)) return null;
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationSanction>(db, sanctionId, token);
            var sanction = await query.SingleOrDefaultAsync(token);
            sanction?.ValidatePersistedState();
            return sanction?.Subject == subject ? sanction : null;
        }

        public async Task<IReadOnlyList<ModerationSanction>> ReadSanctionsAsync(SubjectReference subject, CancellationToken token)
        {
            var rows = await QualifiedSubject(db.ModerationSanctions, subject).AsNoTracking().ToListAsync(token);
            foreach (var row in rows) row.ValidatePersistedState();
            return rows.Where(row => row.Subject == subject && row.Scope.Contains(operation.Target)).ToArray();
        }

        public async Task<IReadOnlyList<ModerationProtectiveMeasure>> ReadProtectionAsync(
            SubjectReference subject, CancellationToken token)
        {
            var rows = await QualifiedSubject(db.ModerationProtectiveMeasures, subject).AsNoTracking().ToListAsync(token);
            foreach (var row in rows) row.ValidatePersistedState();
            return rows.Where(row => row.Subject == subject && row.Scope.Contains(operation.Target)).ToArray();
        }

        private static IQueryable<TEntity> QualifiedSubject<TEntity>(DbSet<TEntity> rows, SubjectReference subject)
            where TEntity : class =>
            rows.Where(entity =>
                EF.Property<ModerationSubjectKind>(entity, nameof(ModerationCase.SubjectKind)) == subject.Kind
                && EF.Property<Guid>(entity, nameof(ModerationCase.SubjectId)) == subject.Id
                && EF.Property<ModerationScopeKind>(entity, nameof(ModerationCase.SubjectOriginKind)) == subject.Origin.Kind
                && EF.Property<Guid>(entity, nameof(ModerationCase.SubjectOriginTrustDomainId)) == subject.TrustDomainId
                && EF.Property<Guid?>(entity, nameof(ModerationCase.SubjectOriginSolutionId))
                    == Ancestor(subject.Origin, ModerationScopeKind.Solution)
                && EF.Property<Guid?>(entity, nameof(ModerationCase.SubjectOriginInstanceId))
                    == Ancestor(subject.Origin, ModerationScopeKind.Instance)
                && EF.Property<Guid?>(entity, nameof(ModerationCase.SubjectOriginTenantId))
                    == Ancestor(subject.Origin, ModerationScopeKind.Tenant)
                && EF.Property<Guid?>(entity, nameof(ModerationCase.SubjectOriginCommunityId))
                    == Ancestor(subject.Origin, ModerationScopeKind.Community)
                && EF.Property<Guid?>(entity, nameof(ModerationCase.SubjectOriginResourceId))
                    == Ancestor(subject.Origin, ModerationScopeKind.Resource)
                && EF.Property<Guid?>(entity, nameof(ModerationCase.SubjectOriginEventId))
                    == Ancestor(subject.Origin, ModerationScopeKind.Event));

        public async Task SaveAsync(
            ModerationDecision decision, ModerationSanction? sanction, ModerationSanction? supersededSanction,
            ModerationAuthorityGrant grant,
            ModerationPlannedEffect effect, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Grant != grant || decision.Subject != operation.Subject || decision.Scope != operation.Target
                || effect.OperationId != operation.OperationId || effect.Id != operation.EffectId
                || effect.DecisionId != decision.Id || effect.DecisionRevision != decision.Revision
                || effect.SanctionId != sanction?.Id || effect.SanctionRevision != sanction?.Revision
                || effect.Subject != decision.Subject || effect.Scope != decision.Scope
                || effect.Kind != operation.Kind || Saved)
            {
                throw new AbortOperation(ModerationOperationResult.Unavailable());
            }

            decision.ValidatePersistedState();
            sanction?.ValidatePersistedState();
            supersededSanction?.ValidatePersistedState();
            if (effect.SupersededSanctionId != supersededSanction?.Id
                || effect.SupersededSanctionRevision != supersededSanction?.Revision
                || supersededSanction is not null && (operation.Kind != ModerationOperationKind.Supersede
                    || supersededSanction.Id != operation.SupersededSanctionId
                    || supersededSanction.Scope != operation.SupersededScope
                    || supersededSanction.Subject != operation.Subject
                    || !grant.Scope.Contains(supersededSanction.Scope)
                    || db.Entry(supersededSanction).State == EntityState.Detached))
            {
                throw new AbortOperation(ModerationOperationResult.Unavailable());
            }
            if (db.Entry(decision).State == EntityState.Detached)
            {
                if (operation.Kind != ModerationOperationKind.Propose
                    || decision.Issuer != grant.Principal || decision.IssuingScope != grant.Scope
                    || operation.Proposal is not { } proposal || decision.CaseId != proposal.CaseId)
                    throw new AbortOperation(ModerationOperationResult.Unavailable());
                db.ModerationDecisions.Add(decision);
            }
            if (sanction is not null && db.Entry(sanction).State == EntityState.Detached)
            {
                if (operation.Kind is not (ModerationOperationKind.Activate or ModerationOperationKind.Supersede))
                    throw new AbortOperation(ModerationOperationResult.Unavailable());
                db.ModerationSanctions.Add(sanction);
            }
            var occurredAt = clock.GetUtcNow();
            var intent = ModerationEffectIntent.Create(
                effect.Id, effect.OperationId, effect.DecisionId, effect.DecisionRevision, effect.SanctionId,
                effect.SanctionRevision, effect.Scope.TrustDomainId, (int)effect.Scope.Kind, effect.Scope.Id,
                (int)effect.Subject.Kind, effect.Subject.Id, (int)effect.Kind,
                grant.PolicyRevision, grant.QualificationRevision, occurredAt,
                effect.SupersededSanctionId, effect.SupersededSanctionRevision);
            db.ModerationEffectIntents.Add(intent);
            db.OutboxMessages.Add(ModerationEffectOutboxMessageFactory.Create(effect, occurredAt));
            var resultRevision = operation.Kind is ModerationOperationKind.Revoke or ModerationOperationKind.Reconcile
                && sanction is not null ? sanction.Revision : decision.Revision;
            Receipt = ModerationOperationReceipt.Complete(
                operation.OperationId, digest, operation.Target.TrustDomainId, (int)operation.Target.Kind,
                operation.Target.Id, (int)operation.Subject.Kind, operation.Subject.Id, grant.Principal.Id,
                PrincipalDigest(grant), decision.Id, sanction?.Id, resultRevision,
                grant.PolicyRevision, grant.QualificationRevision, occurredAt);
            db.ModerationOperationReceipts.Add(Receipt);
            var source = await noticeCapture.CaptureInCurrentTransactionAsync(this, intent.Id, grant, token);
            if (source is null || !source.Matches(intent) || intent.CompletedAt is not null
                || !db.ModerationNoticeSources.Local.Contains(source))
                throw new AbortOperation(ModerationOperationResult.Unavailable());
            ModerationNoticeCaptureSnapshot? captured;
            try { captured = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson); }
            catch (JsonException) { throw new AbortOperation(ModerationOperationResult.Unavailable()); }
            if (captured is null || !captured.Matches(intent) || captured.WriterTenantId != db.TenantFilterTenantId
                || source.RetainUntil != captured.RetainUntil)
                throw new AbortOperation(ModerationOperationResult.Unavailable());
            CaptureValidUntil = captured.ValidUntil;
            OriginalCapture = captured;
            CapturedSource = source;
            NoticeRetention = captured.Notice is { } notice
                ? (notice.CapturedAt, notice.RetainUntil, notice.ContestationCalendarMonths) : null;
            if (!CaptureIsCurrentAt(clock.GetUtcNow()))
                throw new AbortOperation(ModerationOperationResult.Unavailable());
            Saved = true;
        }
    }
}
