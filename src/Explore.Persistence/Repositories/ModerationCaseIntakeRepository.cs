using System.Data;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Case ownership only. Receipt authentication and native target proof remain distinct mandatory
/// owners. The caller owns consumption, acknowledgement, SaveChanges and the final commit.
/// </summary>
public sealed class ModerationCaseIntakeRepository(
    ExploreDbContext context, IModerationCaseTargetReceiptRepository receipts,
    IModerationCaseTargetProofOwner targets, TimeProvider clock) : IModerationCaseIntakeRepository
{
    private StagedCase? _staged;
    private ReadCase? _read;

    public Task<ModerationCaseReadSourcePlan?> DiscoverReadSourcesAsync(
        ModerationCaseAccessRequest request, CancellationToken cancellationToken)
    {
        _read = null;
        return Current(context.Database.CurrentTransaction)
            ? targets.DiscoverReadSourcesAsync(request, cancellationToken)
            : Task.FromResult<ModerationCaseReadSourcePlan?>(null);
    }

    public async Task<ModerationCaseIntakeStage?> StageAsync(
        ModerationCaseAccessRequest request, CancellationToken cancellationToken)
    {
        if (_staged is not null && _staged.Proof.TransactionId != context.Database.CurrentTransaction?.TransactionId)
            _staged = null;
        if (_staged is not null || request.Purpose != ModerationCaseAccessPurpose.SubmitAllegation)
            return null;
        var allegation = await AccessAsync(request, stage: true, cancellationToken);
        return allegation is not null && _staged is { } staged ? new(allegation, staged.Receipt) : null;
    }

    public Task<ModerationCase?> ReadAsync(
        ModerationCaseAccessRequest request, CancellationToken cancellationToken) =>
        request.Purpose == ModerationCaseAccessPurpose.ReadAllegation && request.ExpectedCaseRevision > 0
            ? AccessAsync(request, stage: false, cancellationToken)
            : Task.FromResult<ModerationCase?>(null);

    private async Task<ModerationCase?> AccessAsync(
        ModerationCaseAccessRequest request, bool stage, CancellationToken token)
    {
        _read = null;
        var transaction = context.Database.CurrentTransaction;
        if (!Current(transaction) || request.ReceiptId == Guid.Empty
            || request.ExpectedCaseRevision < 0 || request.PresentedToken.Length != 32
            || stage && (request.OperationId == Guid.Empty || request.CaseId != Guid.Empty || request.ExpectedCaseRevision != 0)
            || !stage && (request.OperationId != Guid.Empty || request.CaseId == Guid.Empty)
            || !await ProviderTenantMatchesAsync(token))
            return null;
        var previousTrust = context.ModerationTrustDomainId;
        var installed = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        if (installed is null || previousTrust is { } previous && previous != installed.TrustDomainId)
            return null;
        try
        {
            // Installation ancestry selects a fail-closed query filter, never permission.
            context.ModerationTrustDomainId = installed.TrustDomainId;
            var receipt = await receipts.ReadForCurrentCallerAsync(request.ReceiptId, request.PresentedToken, token);
            if (receipt is null) return null;
            var observedAt = clock.GetUtcNow();
            var proof = await targets.AcquireAsync(request, receipt, transaction!.TransactionId, observedAt, token);
            if (proof is null || !Matches(request, receipt, proof, observedAt) || !Current(transaction))
                return null;
            // Replay belongs to the immutable reporter acknowledgement, not current case authority.
            if (stage && receipt.ConsumedCaseId is not null)
                return null;

            await using var occurrenceFence = await RelationalNamedLock.AcquireTransactionAsync(context,
                $"moderation.case-occurrence:{proof.Scope.TrustDomainId:D}:{proof.Scope.Id:D}:{proof.Evidence.OccurrenceId:D}", token);
            Guid? existingCaseId = stage
                ? await context.ModerationCases.AsNoTracking().Where(row =>
                    row.ScopeTrustDomainId == proof.Scope.TrustDomainId && row.ScopeTenantId == proof.Scope.Id
                    && row.SubjectId == null && row.OccurrenceId == proof.Evidence.OccurrenceId)
                    .Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token)
                : request.CaseId;
            var caseId = existingCaseId ?? Guid.CreateVersion7();
            await using var caseFence = await RelationalNamedLock.AcquireTransactionAsync(
                context, $"moderation.case-intake:{caseId:D}", token);
            if (existingCaseId is not null)
                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCase>(context, caseId, token);
            if (context.ChangeTracker.Entries<ModerationCase>().Any(entry => entry.Entity.Id == caseId)
                || stage && context.ChangeTracker.Entries<ModerationCaseTargetReceipt>()
                    .Any(entry => entry.Entity.Id == request.ReceiptId))
                return null;
            var allegation = await context.ModerationCases.AsNoTracking().Include(row => row.Evidence)
                .SingleOrDefaultAsync(row => row.Id == caseId, token);
            if (allegation is not null)
            {
                allegation.ValidatePersistedState();
                if (allegation.Subject is not null || allegation.Target != proof.Evidence.Target
                    || allegation.Scope != proof.Scope || allegation.OccurrenceId != proof.Evidence.OccurrenceId)
                    return null;
            }
            if (!await RecheckProofAsync(proof, transaction, token))
                return null;

            if (!stage)
            {
                if (allegation is null || allegation.Revision != request.ExpectedCaseRevision
                    || !allegation.Evidence.Any(row => row.ToReference(
                        proof.Evidence.Target, allegation.OccurrenceId) == proof.Evidence))
                    return null;
                _read = new(request with { Narrative = default }, receipt, proof, transaction!);
                return allegation;
            }

            if (allegation is null)
            {
                allegation = ModerationCase.OpenAllegation(caseId, proof.Scope, proof.Evidence, clock.GetUtcNow());
                context.ModerationCases.Add(allegation);
            }
            else
            {
                // Only the fenced native row supplies the current revision; reporters cannot
                // probe case identity, volume or adjudication state through optimistic concurrency.
                context.ModerationCases.Attach(allegation);
                allegation.RecordEvidence(proof.Evidence, allegation.Revision, clock.GetUtcNow());
            }

            if (context.Entry(allegation).State == EntityState.Detached)
                context.ModerationCases.Attach(allegation);
            context.Attach(receipt);
            _staged = new(request with { Narrative = default }, proof, allegation, receipt, allegation.Revision, transaction!);
            return allegation;
        }
        finally
        {
            context.ModerationTrustDomainId = previousTrust;
        }
    }

    public async Task<bool> RecheckStagedAsync(CancellationToken cancellationToken)
    {
        if (_staged is not { } staged || !Current(staged.Transaction)
            || context.ChangeTracker.HasChanges()
            || staged.Case.Revision != staged.Revision || staged.Case.Subject is not null
            || staged.Case.Target != staged.Proof.Evidence.Target || staged.Case.Scope != staged.Proof.Scope
            || staged.Case.OccurrenceId != staged.Proof.Evidence.OccurrenceId)
            return false;
        var previousTrust = context.ModerationTrustDomainId;
        if (previousTrust is { } previous && previous != staged.Proof.Scope.TrustDomainId)
            return false;
        try
        {
            context.ModerationTrustDomainId = staged.Proof.Scope.TrustDomainId;
            // The caller must have saved receipt consumption and case/acknowledgement together.
            var receipt = await receipts.ReadForCurrentCallerAsync(
                staged.Request.ReceiptId, staged.Request.PresentedToken, cancellationToken);
            return receipt is not null && receipt.ConsumedCaseId == staged.Case.Id
                && receipt.ConsumedOperationId == staged.Request.OperationId
                && Matches(staged.Request, receipt, staged.Proof, staged.Proof.ObservedAt)
                && await RecheckProofAsync(staged.Proof, staged.Transaction, cancellationToken);
        }
        finally
        {
            context.ModerationTrustDomainId = previousTrust;
        }
    }

    public async Task<bool> RecheckReadAsync(CancellationToken cancellationToken)
    {
        if (_read is not { } held) return false;
        if (!Current(held.Transaction)) { _read = null; return false; }
        var previousTrust = context.ModerationTrustDomainId;
        if (previousTrust is { } previous && previous != held.Proof.Scope.TrustDomainId) return false;
        try
        {
            context.ModerationTrustDomainId = held.Proof.Scope.TrustDomainId;
            var receipt = await receipts.ReadForCurrentCallerAsync(
                held.Request.ReceiptId, held.Request.PresentedToken, cancellationToken);
            return receipt is not null && ModerationCaseTargetProofOwner.SameReadReceipt(receipt, held.Receipt)
                && Matches(held.Request, receipt, held.Proof, held.Proof.ObservedAt)
                && await context.ModerationCases.AsNoTracking().AnyAsync(row =>
                row.Id == held.Proof.CaseId && row.Revision == held.Proof.ExpectedCaseRevision, cancellationToken)
                && await RecheckProofAsync(held.Proof, held.Transaction, cancellationToken);
        }
        finally { context.ModerationTrustDomainId = previousTrust; }
    }

    private bool Matches(
        ModerationCaseAccessRequest request, ModerationCaseTargetReceipt receipt,
        ModerationCaseTargetObservation proof, DateTimeOffset observedAt)
    {
        var now = clock.GetUtcNow();
        return proof.TransactionId == context.Database.CurrentTransaction?.TransactionId
        && proof.Purpose == request.Purpose && proof.CaseId == request.CaseId
        && proof.OperationId == request.OperationId && proof.ExpectedCaseRevision == request.ExpectedCaseRevision
        && proof.ReceiptId == receipt.Id && proof.CallerUserId == receipt.CallerUserId
        && proof.HostBindingId == receipt.HostBindingId && proof.QualificationEpochId == receipt.QualificationEpochId
        && proof.QualificationRevision == receipt.QualificationRevision
        && proof.RelationshipSourceId == receipt.RelationshipSourceId
        && proof.RelationshipGeneration == receipt.RelationshipGeneration && proof.Evidence == receipt.Evidence
        && proof.Scope.Kind == ModerationScopeKind.Tenant && proof.Scope.Id == receipt.TenantId
        && proof.Scope.Id == context.TenantFilterTenantId
        && proof.Scope.TrustDomainId == context.ModerationTrustDomainId
        && (proof.Evidence.Target.Origin == proof.Scope || proof.Evidence.Target.Origin == proof.Scope.Parent)
        && (proof.Evidence.SourceOrigin == proof.Scope || proof.Evidence.SourceOrigin == proof.Scope.Parent)
        && proof.ObservedAt == observedAt && observedAt >= receipt.IssuedAt
        && proof.ValidUntil <= receipt.ExpiresAt && observedAt < proof.ValidUntil
        && now >= observedAt && now < proof.ValidUntil;
    }

    private async Task<bool> RecheckProofAsync(
        ModerationCaseTargetObservation proof, IDbContextTransaction transaction, CancellationToken token)
    {
        if (!Current(transaction) || context.TenantFilterTenantId != proof.Scope.Id
            || context.ModerationTrustDomainId != proof.Scope.TrustDomainId
            || !await ProviderTenantMatchesAsync(token))
            return false;
        var now = clock.GetUtcNow();
        if (now < proof.ObservedAt || now >= proof.ValidUntil)
            return false;
        var current = await targets.RecheckAsync(proof, now, token);
        var completedAt = clock.GetUtcNow();
        return current is not null && current.ObservedAt == now
            && current with { ObservedAt = proof.ObservedAt } == proof
            && Current(transaction) && context.TenantFilterTenantId == proof.Scope.Id
            && context.ModerationTrustDomainId == proof.Scope.TrustDomainId
            && completedAt >= now && completedAt < proof.ValidUntil;
    }

    private bool Current(IDbContextTransaction? transaction) =>
        transaction is not null && ReferenceEquals(transaction, context.Database.CurrentTransaction)
        && context.Database.IsRelational() && transaction.GetDbTransaction().IsolationLevel == IsolationLevel.Serializable
        && !context.IsTenantFilterBypassed && context.TenantFilterTenantId is { } tenant && tenant != Guid.Empty;

    private async Task<bool> ProviderTenantMatchesAsync(CancellationToken token)
    {
        if (context.Database.ProviderName != RelationalNamedLock.PostgreSqlProvider)
            return true;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "SELECT current_setting('app.current_tenant_id', true)";
        return await command.ExecuteScalarAsync(token) is string value
            && Guid.TryParse(value, out var tenant) && tenant == context.TenantFilterTenantId;
    }

    private sealed record StagedCase(
        ModerationCaseAccessRequest Request, ModerationCaseTargetObservation Proof, ModerationCase Case,
        ModerationCaseTargetReceipt Receipt, long Revision, IDbContextTransaction Transaction);

    private sealed record ReadCase(ModerationCaseAccessRequest Request, ModerationCaseTargetReceipt Receipt,
        ModerationCaseTargetObservation Proof, IDbContextTransaction Transaction)
    {
        public override string ToString() => nameof(ReadCase);
    }
}
