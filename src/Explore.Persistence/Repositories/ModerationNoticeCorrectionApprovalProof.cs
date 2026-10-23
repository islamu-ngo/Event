using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>Authenticates retained correction approval provenance, never current disclosure or reading authority.</summary>
internal static class ModerationNoticeCorrectionApprovalProof
{
    internal static async Task<bool> MatchesAsync(
        ExploreDbContext context, ModerationNoticeDisclosureApproval approval, ModerationNoticeSource source,
        ModerationNoticeCaptureSnapshot capture, CancellationToken token)
    {
        try
        {
            var storedApproval = await context.ModerationNoticeDisclosureApprovals.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == approval.Id, token);
            var storedSource = await context.ModerationNoticeSources.AsNoTracking()
                .SingleOrDefaultAsync(row => row.EffectId == source.EffectId, token);
            if (storedApproval is null || storedSource is null
                || JsonSerializer.Serialize(storedApproval) != JsonSerializer.Serialize(approval)
                || JsonSerializer.Serialize(storedSource) != JsonSerializer.Serialize(source))
                return false;
            approval = storedApproval;
            source = storedSource;
            approval.ValidatePersistedState();
            var intent = approval.Intent;
            var effect = await context.ModerationEffectIntents.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == source.EffectId, token);
            if (effect is null || effect.OperationKind != (int)ModerationOperationKind.Revoke
                || effect.CompletedAt is not null || effect.OriginalCompletionId is not null || effect.OriginalNoticeCleanupId is not null
                || !source.Matches(effect) || !capture.Matches(effect)
                || JsonSerializer.Serialize(capture) != JsonSerializer.Serialize(
                    JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson))
                || capture.PendingCorrection is not { } pending || capture.Notice is not null
                || approval.EffectId != source.EffectId || approval.DecisionId != source.DecisionId
                || approval.DecisionRevision != source.DecisionRevision || approval.CaseId != capture.Decision.CaseId
                || approval.TrustDomainId != source.TrustDomainId || approval.HostBindingId != capture.HostBindingId
                || approval.TenantId != capture.WriterTenantId
                || source.TenantId is { } sourceTenant && sourceTenant != approval.TenantId
                || approval.RetainUntil != source.RetainUntil || approval.RetainUntil != pending.RetainUntil
                || approval.ApprovedAt < source.CapturedAt
                || approval.WriterGeneration < 2
                || !intent.ContentRepresentatives.IsEmpty)
                return false;

            var host = await context.ModerationHostBindings.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == approval.HostBindingId, token);
            var decision = await context.ModerationDecisions.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == source.DecisionId, token);
            var allegation = await context.ModerationCases.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == approval.CaseId, token);
            if (host is null || host.TrustDomainId != approval.TrustDomainId
                || host.SolutionId != capture.HostSolutionId || host.InstanceId != capture.HostInstanceId
                || decision is null || decision.CaseId != approval.CaseId
                || decision.Revision < approval.DecisionRevision || allegation is null
                || allegation.Revision < approval.CaseRevision
                || allegation.Scope != decision.Scope && (allegation.AttributionSourceId is null
                    || decision.Scope.Kind != ModerationScopeKind.Resource || decision.Scope.Parent != allegation.Scope)
                || allegation.Subject != decision.Subject
                || ModerationNoticeScope.From(decision.Scope) != capture.Decision.Scope
                || ModerationNoticeScope.From(decision.Subject.Origin) != capture.Decision.SubjectOrigin
                || (int)decision.SubjectKind != source.SubjectKind || decision.SubjectId != source.SubjectId)
                return false;
            var instance = AuthorityScope.Instance(AuthorityScope.Solution(
                AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId);
            if (decision.Scope != instance && !AuthorityScope.Tenant(instance, approval.TenantId).Contains(decision.Scope))
                return false;

            var originalEffect = await context.ModerationEffectIntents.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == pending.OriginalEffectId, token);
            var original = await context.ModerationNoticeSources.AsNoTracking()
                .SingleOrDefaultAsync(row => row.EffectId == pending.OriginalEffectId, token);
            if (originalEffect is null || original is null
                || originalEffect.OriginalNoticeCleanupId is not null
                || originalEffect.OperationKind is not ((int)ModerationOperationKind.Activate) and not ((int)ModerationOperationKind.Supersede)
                || !original.Matches(originalEffect) || original.SnapshotHash != pending.OriginalSnapshotHash
                || original.EffectBindingHash != pending.OriginalEffectBindingHash
                || original.TrustDomainId != source.TrustDomainId || original.DecisionId != source.DecisionId
                || original.DecisionRevision != source.DecisionRevision || original.ScopeKind != source.ScopeKind
                || original.ScopeId != source.ScopeId || original.SubjectKind != source.SubjectKind
                || original.SubjectId != source.SubjectId || original.CapturedAt > source.CapturedAt
                || original.TenantId is { } originalTenant && originalTenant != approval.TenantId
                || original.RetainUntil != pending.RetainUntil
                || originalEffect.SanctionId != effect.SanctionId || originalEffect.SanctionRevision >= effect.SanctionRevision)
                return false;
            var originalCapture = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(original.SnapshotJson);
            if (originalCapture is null || !originalCapture.Matches(originalEffect)
                || originalCapture.PendingCorrection is not null || originalCapture.Notice is not { } notice
                || originalCapture.HostBindingId != host.Id || originalCapture.HostSolutionId != host.SolutionId
                || originalCapture.HostInstanceId != host.InstanceId || originalCapture.WriterTenantId != approval.TenantId
                || originalCapture.Decision.CaseId != approval.CaseId
                || originalCapture.Decision.Scope != capture.Decision.Scope
                || originalCapture.Decision.SubjectOrigin != capture.Decision.SubjectOrigin
                || notice.RetainUntil != pending.RetainUntil)
                return false;
            var originalApprovals = await context.ModerationNoticeDisclosureApprovals.AsNoTracking()
                .Where(row => row.EffectId == original.EffectId).Take(2).ToArrayAsync(token);
            if (originalApprovals.Length != 1) return false;
            var originalApproval = originalApprovals[0];
            originalApproval.ValidatePersistedState();
            if (originalApproval.Id != notice.ApprovalId || notice.ApprovalRevision != 1
                || originalApproval.HostBindingId != host.Id || originalApproval.TrustDomainId != host.TrustDomainId
                || originalApproval.TenantId != approval.TenantId || originalApproval.RetainUntil != pending.RetainUntil
                || originalApproval.DecisionId != source.DecisionId || originalApproval.CaseId != approval.CaseId)
                return false;

            return await MatchesReceiptAsync(context, approval, source, decision.Scope, token);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    // Callers qualifying a live read must authenticate custody and current approval before this receipt-only join.
    internal static async Task<bool> MatchesReceiptAsync(ExploreDbContext context,
        ModerationNoticeDisclosureApproval approval, ModerationNoticeSource source, AuthorityScope target, CancellationToken token)
    {
        try
        {
            var receipts = await context.ModerationQualificationMutationReceipts.AsNoTracking()
                .Where(row => row.SourceId == approval.Id && row.SourceKind == ModerationQualificationSourceKind.NoticeDisclosure
                    && row.ResultRevision == 1).Take(2).ToArrayAsync(token);
            if (receipts.Length != 1) return false;
            var receipt = receipts[0];
            receipt.ValidatePersistedState();
            if (receipt.HostBindingId != approval.HostBindingId || receipt.TrustDomainId != approval.TrustDomainId
                || receipt.TenantId != approval.TenantId || receipt.PrincipalId != approval.ApprovedByHumanId
                || receipt.QualificationRevision != approval.WriterGeneration || receipt.CompletedAt != approval.ApprovedAt)
                return false;
            var request = new ApproveModerationNoticeCorrectionDisclosureSource
            {
                OperationId = receipt.Id,
                Target = target,
                ExpectedQualificationRevision = approval.WriterGeneration - 1,
                Intent = approval.Intent,
                ExpectedPendingSnapshotHash = source.SnapshotHash,
                ExpectedPendingEffectBindingHash = source.EffectBindingHash
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
}
