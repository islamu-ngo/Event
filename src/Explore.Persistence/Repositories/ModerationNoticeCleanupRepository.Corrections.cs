using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using ISLAMU.Moderation.Application;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationNoticeCleanupRepository
{
    private async Task<ModerationNoticeDisclosureApproval[]?> ResolveOriginalCleanupApprovalsAsync(
        ModerationNoticeDisclosureApproval[] due, ModerationHostBinding host, Guid tenantId, CancellationToken token)
    {
        var originals = new Dictionary<Guid, ModerationNoticeDisclosureApproval>();
        foreach (var approval in due)
        {
            var source = await context.ModerationNoticeSources.AsNoTracking()
                .SingleOrDefaultAsync(row => row.EffectId == approval.EffectId, token);
            var capture = source is null ? null : ReadCleanupCapture(source);
            if (capture?.PendingCorrection is not { } pending)
            {
                // A missing/corrupt pending Revoke cannot be reclassified as an ordinary original.
                if (await context.ModerationEffectIntents.AsNoTracking()
                    .AnyAsync(row => row.Id == approval.EffectId && row.OperationKind == (int)ModerationOperationKind.Revoke, token))
                {
                    if (capture?.Notice is null || !await OriginalDisclosureReceiptMatchesAsync(approval, token)) return null;
                }
                originals.TryAdd(approval.Id, approval);
                continue;
            }
            if (source is null || capture is null
                || !await ModerationNoticeCorrectionApprovalProof.MatchesAsync(context, approval, source, capture, token))
                return null;
            // Join at most one creating approval per scanned row, without widening the scan/cursor.
            // Authenticate before discovery so every original fence precedes every dependent fence.
            var creating = await context.ModerationNoticeDisclosureApprovals.AsNoTracking()
                .Where(row => row.EffectId == pending.OriginalEffectId).Take(2).ToArrayAsync(token);
            if (creating.Length != 1 || creating[0].HostBindingId != host.Id
                || creating[0].TrustDomainId != host.TrustDomainId || creating[0].TenantId != tenantId
                || creating[0].RetainUntil != approval.RetainUntil)
                return null;
            originals.TryAdd(creating[0].Id, creating[0]);
        }
        return originals.Values.OrderBy(row => row.Id).ToArray();
    }

    private async Task<bool> OriginalDisclosureReceiptMatchesAsync(ModerationNoticeDisclosureApproval approval, CancellationToken token)
    {
        // A rewritten full-notice capture must not disguise a retained text-only approval receipt.
        // Existing full Revoke captures without a qualification receipt retain their original cleanup path.
        var receipts = await context.ModerationQualificationMutationReceipts.AsNoTracking()
            .Where(row => row.SourceId == approval.Id && row.ResultRevision == 1).Take(2).ToArrayAsync(token);
        if (receipts.Length == 0) return true;
        if (receipts.Length != 1) return false;
        try
        {
            approval.ValidatePersistedState();
            var receipt = receipts[0];
            receipt.ValidatePersistedState();
            var decision = await context.ModerationDecisions.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == approval.DecisionId, token);
            if (decision is null || receipt.SourceKind != ModerationQualificationSourceKind.NoticeDisclosure
                || receipt.HostBindingId != approval.HostBindingId || receipt.TrustDomainId != approval.TrustDomainId
                || receipt.TenantId != approval.TenantId || receipt.PrincipalId != approval.ApprovedByHumanId
                || receipt.QualificationRevision != approval.WriterGeneration || receipt.CompletedAt != approval.ApprovedAt)
                return false;
            var request = new ApproveModerationNoticeDisclosureSource
            {
                OperationId = receipt.Id,
                Target = decision.Scope,
                ExpectedQualificationRevision = approval.WriterGeneration - 1,
                Intent = approval.Intent
            };
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(Encoding.UTF8.GetBytes(request.GetType().FullName + "\n"));
            var payload = JsonSerializer.SerializeToUtf8Bytes(request, request.GetType());
            try
            {
                hash.AppendData(payload);
                return receipt.RequestDigest == Convert.ToHexString(hash.GetHashAndReset());
            }
            finally { CryptographicOperations.ZeroMemory(payload); }
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private async Task<PendingCorrectionSource[]?> DiscoverPendingCorrectionsAsync(
        ModerationNoticeDisclosureApproval[] approvals, ModerationHostBinding host, Guid tenantId, CancellationToken token)
    {
        if (approvals.Length == 0) return [];
        var originals = approvals.Select(row => row.EffectId).ToArray();
        var creating = await context.ModerationEffectIntents.AsNoTracking()
            .Where(row => originals.Contains(row.Id) && row.TrustDomainId == host.TrustDomainId
                && row.SanctionId != null && (row.OperationKind == (int)ModerationOperationKind.Activate
                    || row.OperationKind == (int)ModerationOperationKind.Supersede))
            .Select(row => new { row.Id, row.SanctionId }).ToArrayAsync(token);
        if (creating.Length == 0) return [];
        var sanctions = creating.Select(row => row.SanctionId!.Value).Distinct().ToArray();
        // The canonical sanction relationship selects dependencies; their JSON anchor and expiry
        // are evidence to validate, never discovery filters that could hide a divergent dependent.
        var effects = await context.ModerationEffectIntents.AsNoTracking()
            .Where(row => row.TrustDomainId == host.TrustDomainId && row.SanctionId != null
                && sanctions.Contains(row.SanctionId.Value) && row.OperationKind == (int)ModerationOperationKind.Revoke)
            .OrderBy(row => row.Id).Take(1025).ToArrayAsync(token);
        if (effects.Length > 1024) return null;
        var pending = new List<PendingCorrectionSource>();
        foreach (var effect in effects)
        {
            var source = await context.ModerationNoticeSources.AsNoTracking()
                .SingleOrDefaultAsync(row => row.EffectId == effect.Id, token);
            if (source is null)
            {
                // Independently approved Revoke payload may expire before its creating payload.
                if (!effect.HasOriginalNoticeCleanup(host.Id, tenantId, clock.GetUtcNow())
                    || await context.ModerationNotices.AsNoTracking().AnyAsync(row => row.EffectId == effect.Id, token)
                    || await context.ModerationNoticeDisclosureApprovals.AsNoTracking().AnyAsync(row => row.EffectId == effect.Id, token)
                    || await context.ModerationNoticeNotificationReceipts.AsNoTracking().AnyAsync(row => row.EffectId == effect.Id, token)
                    || await context.Set<ModerationContentResponse>().AsNoTracking().AnyAsync(row => row.EffectId == effect.Id, token)
                    || await HasNativeCopiesAsync(tenantId, effect.Id, token)) return null;
                continue;
            }
            if (source.TenantId is { } sourceTenant && sourceTenant != tenantId || !source.Matches(effect)) return null;
            var capture = ReadCleanupCapture(source);
            if (capture is null || !capture.Matches(effect) || capture.WriterTenantId != tenantId
                || capture.HostBindingId != host.Id || capture.HostSolutionId != host.SolutionId
                || capture.HostInstanceId != host.InstanceId) return null;
            if (capture.PendingCorrection is null)
            {
                // A legacy full Revoke notice is independent; text-only approval never enters this branch.
                var independent = await context.ModerationNoticeDisclosureApprovals.AsNoTracking()
                    .Where(row => row.EffectId == effect.Id && row.HostBindingId == host.Id
                        && row.TenantId == tenantId && row.RetainUntil == source.RetainUntil)
                    .Take(2).ToArrayAsync(token);
                if (capture.Notice is null || independent.Length != 1
                    || !await OriginalDisclosureReceiptMatchesAsync(independent[0], token)) return null;
                continue;
            }
            var owners = creating.Where(row => row.SanctionId == effect.SanctionId).ToArray();
            if (owners.Length != 1) return null;
            var approvalsForCorrection = await context.ModerationNoticeDisclosureApprovals.AsNoTracking()
                .Where(row => row.EffectId == effect.Id).Take(2).ToArrayAsync(token);
            if (approvalsForCorrection.Length > 1 || approvalsForCorrection.Length == 1
                && !await ModerationNoticeCorrectionApprovalProof.MatchesAsync(
                    context, approvalsForCorrection[0], source, capture, token)) return null;
            pending.Add(new(source.EffectId, owners[0].Id, source.SnapshotHash, source.EffectBindingHash,
                approvalsForCorrection.SingleOrDefault()?.Id));
        }
        return pending.ToArray();
    }

    private async Task<ImmutableArray<RemovedCorrection>?> StagePendingCorrectionCleanupAsync(
        ModerationNoticeDisclosureApproval approval, ModerationNoticeSource? originalSource,
        ModerationEffectIntent? originalEffect, IReadOnlyList<ModerationNotice> originalOccurrences,
        PendingCorrectionSource[] candidates, ModerationHostBinding host, CancellationToken token)
    {
        var dependents = candidates.Where(row => row.OriginalEffectId == approval.EffectId).OrderBy(row => row.EffectId).ToArray();
        if (dependents.Length == 0) return ImmutableArray<RemovedCorrection>.Empty;
        var originalCapture = originalSource is null ? null : ReadCleanupCapture(originalSource);
        if (originalSource is null || originalEffect is null || !originalSource.Matches(originalEffect)
            || originalSource.TenantId is { } originalTenant && originalTenant != approval.TenantId
            || originalCapture is null || !originalCapture.Matches(originalEffect)
            || originalCapture.Notice is null || originalCapture.PendingCorrection is not null
            || originalCapture.HostBindingId != host.Id || originalCapture.HostSolutionId != host.SolutionId
            || originalCapture.HostInstanceId != host.InstanceId || originalCapture.WriterTenantId != approval.TenantId
            || originalCapture.RetainUntil != approval.RetainUntil
            || originalEffect.OperationKind is not ((int)ModerationOperationKind.Activate) and not ((int)ModerationOperationKind.Supersede))
            return null;
        var removed = ImmutableArray.CreateBuilder<RemovedCorrection>();
        foreach (var discovered in dependents)
        {
            var source = await context.ModerationNoticeSources.SingleOrDefaultAsync(row => row.EffectId == discovered.EffectId, token);
            var effect = await context.ModerationEffectIntents.SingleOrDefaultAsync(row => row.Id == discovered.EffectId, token);
            var capture = source is null ? null : ReadCleanupCapture(source);
            if (source is null || effect is null || capture is null || !source.Matches(effect) || !capture.Matches(effect)
                || source.SnapshotHash != discovered.SnapshotHash || source.EffectBindingHash != discovered.EffectBindingHash
                || source.TrustDomainId != host.TrustDomainId
                || source.TenantId is { } correctionTenant && correctionTenant != approval.TenantId
                || source.DecisionId != originalSource.DecisionId || source.ScopeKind != originalSource.ScopeKind
                || source.ScopeId != originalSource.ScopeId || source.SubjectKind != originalSource.SubjectKind
                || source.SubjectId != originalSource.SubjectId || source.CapturedAt < originalSource.CapturedAt
                || source.RetainUntil != approval.RetainUntil || capture.RetainUntil != approval.RetainUntil
                || capture.HostBindingId != host.Id || capture.HostSolutionId != host.SolutionId
                || capture.HostInstanceId != host.InstanceId || capture.WriterTenantId != approval.TenantId
                || capture.Decision.CaseId != originalCapture.Decision.CaseId
                || capture.Decision.Scope != originalCapture.Decision.Scope
                || capture.Decision.SubjectOrigin != originalCapture.Decision.SubjectOrigin
                || effect.SanctionId != originalEffect.SanctionId || effect.SanctionRevision <= originalEffect.SanctionRevision
                || effect.CompletedAt is not null || effect.OriginalCompletionId is not null || effect.OriginalNoticeCleanupId is not null
                || capture.PendingCorrection is not { } pending || pending.OriginalEffectId != originalEffect.Id
                || pending.OriginalSnapshotHash != originalSource.SnapshotHash
                || pending.OriginalEffectBindingHash != originalSource.EffectBindingHash
                || pending.RetainUntil != approval.RetainUntil || pending.Recipients.Length != originalOccurrences.Count
                || !pending.Recipients.All(recipient => originalOccurrences.Count(row =>
                    row.Id == recipient.OriginalNoticeId && row.EffectId == originalSource.EffectId
                    && row.TrustDomainId == originalSource.TrustDomainId && row.TenantId == originalSource.TenantId
                    && row.CreatedAt == originalSource.CapturedAt && row.Audience == recipient.Audience
                    && row.RecipientSubjectKind == recipient.RecipientSubjectKind
                    && row.RecipientSubjectId == recipient.RecipientSubjectId) == 1))
                return null;
            var occurrences = await context.ModerationNotices.Where(row => row.EffectId == effect.Id)
                .OrderBy(row => row.Id).Take(1025).ToArrayAsync(token);
            if (occurrences.Length > 1024 || occurrences.Length != pending.Recipients.Length
                || occurrences.Any(row => row.TrustDomainId != source.TrustDomainId || row.TenantId != source.TenantId
                    || row.CreatedAt != source.CapturedAt || row.NativeIntentId is not null
                    || row.DeliveryDisposition != ModerationNoticeDeliveryDisposition.RecipientUnavailable)
                || !pending.Recipients.All(recipient => occurrences.Count(row => row.Audience == recipient.Audience
                    && row.RecipientSubjectKind == recipient.RecipientSubjectKind
                    && row.RecipientSubjectId == recipient.RecipientSubjectId) == 1)
                || await context.ModerationNoticeNotificationReceipts.AsNoTracking().AnyAsync(row => row.EffectId == effect.Id, token)
                || await context.Set<ModerationContentResponse>().AsNoTracking().AnyAsync(row => row.EffectId == effect.Id, token)
                || await HasNativeCopiesAsync(approval.TenantId, effect.Id, token))
                return null;
            var correctionApprovals = await context.ModerationNoticeDisclosureApprovals
                .Where(row => row.EffectId == effect.Id).Take(2).ToArrayAsync(token);
            if (correctionApprovals.Length > 1
                || correctionApprovals.SingleOrDefault()?.Id != discovered.ApprovalId
                || correctionApprovals.Length == 1 && !await ModerationNoticeCorrectionApprovalProof.MatchesAsync(
                    context, correctionApprovals[0], source, capture, token))
                return null;
            context.RemoveRange(correctionApprovals);
            context.RemoveRange(occurrences);
            context.Remove(source);
            removed.Add(new(effect.Id, effect.Revision, discovered.ApprovalId));
        }
        return removed.ToImmutable();
    }

    private async Task<bool> PendingCorrectionsRemovedAsync(
        ImmutableArray<RemovedCorrection> corrections, Guid tenantId, CancellationToken token)
    {
        foreach (var correction in corrections)
        {
            if (await context.ModerationNoticeSources.AsNoTracking().AnyAsync(row => row.EffectId == correction.EffectId, token)
                || await context.ModerationNotices.AsNoTracking().AnyAsync(row => row.EffectId == correction.EffectId, token)
                || await context.ModerationNoticeDisclosureApprovals.AsNoTracking().AnyAsync(
                    row => row.EffectId == correction.EffectId || row.Id == correction.ApprovalId, token)
                || await context.ModerationNoticeNotificationReceipts.AsNoTracking().AnyAsync(row => row.EffectId == correction.EffectId, token)
                || await context.Set<ModerationContentResponse>().AsNoTracking().AnyAsync(row => row.EffectId == correction.EffectId, token)
                || await HasNativeCopiesAsync(tenantId, correction.EffectId, token))
                return false;
            var effect = await context.ModerationEffectIntents.AsNoTracking().SingleOrDefaultAsync(row => row.Id == correction.EffectId, token);
            if (effect is null || effect.Revision != correction.Revision || effect.CompletedAt is not null
                || effect.OriginalNoticeCleanupId is not null || effect.OriginalCompletionId is not null)
                return false;
        }
        return true;
    }

    private static ModerationNoticeCaptureSnapshot? ReadCleanupCapture(ModerationNoticeSource source)
    {
        if (source.SnapshotJson.Length > 128 * 1024) return null;
        try { return JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson); }
        catch (JsonException) { return null; }
    }

    private sealed record PendingCorrectionSource(
        Guid EffectId, Guid OriginalEffectId, string SnapshotHash, string EffectBindingHash, Guid? ApprovalId);
    private sealed record RemovedCorrection(Guid EffectId, long Revision, Guid? ApprovalId);
}
