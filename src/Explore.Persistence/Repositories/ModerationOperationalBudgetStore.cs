using System.Data;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Transaction-local Human intake capacity, derived from original durable acceptances rather
/// than a second counter or an expiring permit. Independent native target authority remains required.
/// </summary>
internal sealed class ModerationOperationalBudgetStore(
    ExploreDbContext context, IAdminContext admin, IMachinePrincipalAccessor machine, TimeProvider clock)
{
    private Lease? lease;
    private Guid? occurrenceId;
    private ModerationCaseIntakeAcknowledgement? acceptance;
    private IReadOnlyList<ModerationOperationalOccurrence>? priorHistory;

    internal async Task<bool> AcquireAsync(Guid receiptId, CancellationToken token)
    {
        lease = null;
        occurrenceId = null;
        acceptance = null;
        priorHistory = null;
        var transaction = context.Database.CurrentTransaction;
        if (transaction is null || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || context.IsTenantFilterBypassed || context.TenantFilterTenantId is not { } tenantId
            || machine.IsMachineCaller || await admin.ResolveUserIdAsync(token) is not { } humanId)
            return false;
        var receipt = await context.ModerationCaseTargetReceipts.AsNoTracking().SingleOrDefaultAsync(row =>
            row.Id == receiptId && row.CallerUserId == humanId && row.TenantId == tenantId, token);
        if (receipt is null) return false;
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        if (host is null || receipt.HostBindingId != host.Id) return false;
        var instance = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId);
        var tenant = AuthorityScope.Tenant(instance, tenantId);
        var authority = await new ModerationQualificationAuthority(context, admin, machine)
            .AcquireCaseNarrativeAsync(tenant, receipt.TargetKind, token);
        if (authority is null || authority.UserId != humanId || authority.Binding.Id != host.Id
            || authority.Epoch.Id != receipt.QualificationEpochId || authority.Epoch.Revision != receipt.QualificationRevision)
            return false;
        var current = await new ModerationOperationalBudgetReader(context, clock)
            .ReadAsync(authority, ModerationOperationalAction.SubmitReport, token);
        if (current is null) return false;
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, humanId, instance);
        // Only canonical acting identity, scope, mandate absence and action define this bucket.
        // Policy revision, credential, target, material and request ID never reset its history.
        var coordinates = FormattableString.Invariant(
            $"{host.TrustDomainId:N}:{host.SolutionId:N}:{host.InstanceId:N}:{tenantId:N}:{humanId:N}:human:none:submit-report");
        var bucket = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(coordinates)));
        await using var held = await RelationalNamedLock.AcquireTransactionAsync(context,
            $"moderation.operational-budget:{bucket}", token);
        var now = clock.GetUtcNow();
        if (!current.Value.Adoption.MatchesCurrent(current.Value.Source, now)) return false;
        lease = new(transaction, authority, current.Value.Source, current.Value.Adoption, receipt, subject, now);
        return EnvironmentCurrent(lease);
    }

    internal async Task<bool> AssessAsync(ModerationCaseIntakeStage stage, CancellationToken token)
    {
        if (lease is not { } held || occurrenceId is not null || !EnvironmentCurrent(held)
            || stage.Receipt.Id != held.Receipt.Id || stage.Receipt.Evidence != held.Receipt.Evidence
            || stage.Receipt.CallerUserId != held.Authority.UserId || stage.Case.Scope != held.Authority.Tenant
            || stage.Case.Target != held.Receipt.Evidence.Target)
            return false;
        var history = await HistoryAsync(held, token);
        if (history is null) return false;
        var now = clock.GetUtcNow();
        if (now < held.ObservedAt || !held.Adoption.MatchesCurrent(held.Source, now)) return false;
        var assessment = Budget(held).Assess(stage.Receipt.OccurrenceId, now, history);
        if (assessment.State == ModerationOperationalBudgetState.Exhausted) return false;
        occurrenceId = stage.Receipt.OccurrenceId;
        priorHistory = history;
        return true;
    }

    internal ModerationCaseIntakeAcknowledgement Capture(
        Guid id, ModerationCaseIntakeStage stage, ReadOnlySpan<byte> digest)
    {
        if (lease is not { } held || occurrenceId != stage.Receipt.OccurrenceId || acceptance is not null
            || !EnvironmentCurrent(held))
            throw new InvalidOperationException("Intake acceptance requires its current transaction-local budget assessment.");
        acceptance = ModerationCaseIntakeAcknowledgement.Capture(
            id, stage.Case, stage.Receipt, digest, held.Source, held.Adoption);
        return acceptance;
    }

    internal async Task<bool> RecheckAsync(CancellationToken token)
    {
        if (lease is not { } held || acceptance is not { } captured || occurrenceId is not { } occurrence
            || priorHistory is not { } before
            || !EnvironmentCurrent(held) || context.ChangeTracker.HasChanges())
            return false;
        var authority = await new ModerationQualificationAuthority(context, admin, machine)
            .AcquireCaseNarrativeAsync(held.Authority.Tenant, held.Receipt.TargetKind, token);
        if (authority is null || authority.UserId != held.Authority.UserId || authority.Binding.Id != held.Authority.Binding.Id
            || authority.Epoch.Id != held.Authority.Epoch.Id || authority.Epoch.Revision != held.Receipt.QualificationRevision)
            return false;
        var current = await new ModerationOperationalBudgetReader(context, clock)
            .ReadAsync(authority, ModerationOperationalAction.SubmitReport, token);
        if (current?.Source.Id != held.Source.Id || current.Value.Source.WriterGeneration != held.Source.WriterGeneration
            || current.Value.Adoption.Id != held.Adoption.Id
            || current.Value.Adoption.WriterGeneration != held.Adoption.WriterGeneration)
            return false;
        var persisted = await context.ModerationCaseIntakeAcknowledgements.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == captured.Id, token);
        if (persisted is null || persisted.ReceiptId != captured.ReceiptId || persisted.OperationId != captured.OperationId
            || persisted.CallerUserId != captured.CallerUserId || persisted.CaseId != captured.CaseId
            || persisted.RequestDigest != captured.RequestDigest || persisted.AcceptedAt != captured.AcceptedAt
            || persisted.BudgetSourceId != held.Source.Id || persisted.BudgetAdoptionId != held.Adoption.Id
            || persisted.BudgetSourceWriterGeneration != held.Source.WriterGeneration
            || persisted.BudgetAdoptionWriterGeneration != held.Adoption.WriterGeneration)
            return false;
        var history = await HistoryAsync(held, token);
        if (history is null) return false;
        var original = before.SingleOrDefault(row => row.Id == occurrence);
        var expected = before.Where(row => row.Id != occurrence).Append(new ModerationOperationalOccurrence(
            occurrence, held.Subject, held.Authority.Tenant, null, ModerationOperationalAction.SubmitReport,
            original?.RecordedAt ?? captured.AcceptedAt)).ToHashSet();
        if (history.Count != expected.Count || !expected.SetEquals(history)) return false;
        var now = clock.GetUtcNow();
        if (now < held.ObservedAt || !current.Value.Adoption.MatchesCurrent(current.Value.Source, now)) return false;
        var assessment = Budget(held).Assess(occurrence, now, history);
        return assessment.State == ModerationOperationalBudgetState.Duplicate
            && (original is not null || assessment.DistinctOccurrences <= held.Source.MaximumOccurrences)
            && EnvironmentCurrent(held);
    }

    private async Task<IReadOnlyList<ModerationOperationalOccurrence>?> HistoryAsync(Lease held, CancellationToken token)
    {
        if (await context.ModerationCaseTargetReceipts.AsNoTracking().AnyAsync(receipt =>
            receipt.CallerUserId == held.Authority.UserId && receipt.TenantId == held.Authority.Tenant.Id
            && receipt.SourceOriginTrustDomainId == held.Authority.Binding.TrustDomainId
            && receipt.SourceOriginSolutionId == held.Authority.Binding.SolutionId
            && receipt.SourceOriginInstanceId == held.Authority.Binding.InstanceId
            && receipt.ConsumedCaseId != null && !context.ModerationCaseIntakeAcknowledgements.Any(acknowledgement =>
                acknowledgement.ReceiptId == receipt.Id && acknowledgement.CallerUserId == receipt.CallerUserId
                && acknowledgement.TenantId == receipt.TenantId), token))
            return null;
        // Keep all original acceptance history: a rolling-window-only query would lose old
        // duplicates. No private narrative, network identity or new person fingerprint is read.
        var accepted = await (
            from acknowledgement in context.ModerationCaseIntakeAcknowledgements.AsNoTracking()
            join receipt in context.ModerationCaseTargetReceipts.AsNoTracking()
                on acknowledgement.ReceiptId equals receipt.Id into receipts
            from receipt in receipts.DefaultIfEmpty()
            where acknowledgement.CallerUserId == held.Authority.UserId
                && acknowledgement.TenantId == held.Authority.Tenant.Id
                && acknowledgement.TrustDomainId == held.Authority.Binding.TrustDomainId
            select new { Acknowledgement = acknowledgement, Receipt = receipt }).ToListAsync(token);
        var original = new Dictionary<Guid, DateTimeOffset>();
        foreach (var row in accepted)
        {
            var receipt = row.Receipt;
            if (receipt is null) return null;
            var acknowledgement = row.Acknowledgement;
            acknowledgement.ValidatePersistedState();
            receipt.ValidatePersistedState();
            if (receipt.SourceOriginTrustDomainId != held.Authority.Instance.TrustDomainId
                || receipt.SourceOriginSolutionId != held.Authority.Binding.SolutionId
                || receipt.SourceOriginInstanceId != held.Authority.Binding.InstanceId)
                continue;
            if (receipt.CallerUserId != acknowledgement.CallerUserId || receipt.TenantId != acknowledgement.TenantId
                || receipt.ConsumedCaseId != acknowledgement.CaseId || receipt.ConsumedOperationId != acknowledgement.OperationId
                || receipt.Revision != acknowledgement.ReceiptRevision || receipt.ConsumedAt != acknowledgement.AcceptedAt)
                return null;
            if (!original.TryGetValue(receipt.OccurrenceId, out var at) || acknowledgement.AcceptedAt < at)
                original[receipt.OccurrenceId] = acknowledgement.AcceptedAt;
        }
        return original.Select(row => new ModerationOperationalOccurrence(
            row.Key, held.Subject, held.Authority.Tenant, null, ModerationOperationalAction.SubmitReport, row.Value)).ToArray();
    }

    private static ModerationOperationalBudget Budget(Lease held) => new(
        held.Subject, held.Authority.Tenant, null, ModerationOperationalAction.SubmitReport,
        held.Source.MaximumOccurrences, TimeSpan.FromTicks(held.Source.WindowTicks));

    private bool EnvironmentCurrent(Lease held) =>
        ReferenceEquals(context.Database.CurrentTransaction, held.Transaction) && !machine.IsMachineCaller
        && !context.IsTenantFilterBypassed && context.TenantFilterTenantId == held.Authority.Tenant.Id
        && context.ModerationTrustDomainId == held.Authority.Binding.TrustDomainId;

    private sealed record Lease(IDbContextTransaction Transaction, ModerationSourceAuthority Authority,
        ModerationOperationalBudgetSource Source, ModerationOperationalBudgetAdoption Adoption,
        ModerationCaseTargetReceipt Receipt, SubjectReference Subject, DateTimeOffset ObservedAt);
}
