using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    private async Task<QualifiedSource> ApproveCorrectionNoticeAsync(
        ApproveModerationNoticeCorrectionDisclosureSource request, ModerationSourceAuthority authority,
        DateTimeOffset observedAt, CancellationToken token)
    {
        var deadline = await QualifyCorrectionNoticeAsync(request, authority, token);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            $"moderation.disclosure:{authority.Binding.Id:N}:{request.Intent.EffectId:N}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationNoticeDisclosureApproval>(context, request.Intent.ApprovalId, token);
        if (await context.ModerationNoticeDisclosureApprovals.AsNoTracking().AnyAsync(
            row => row.Id == request.Intent.ApprovalId || row.EffectId == request.Intent.EffectId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        if (clock.GetUtcNow() >= deadline || clock.GetUtcNow() < observedAt)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        context.ModerationNoticeDisclosureApprovals.Add(ModerationNoticeDisclosureApproval.Approve(
            request.Intent, authority.Binding.Id, authority.Binding.TrustDomainId, authority.Tenant.Id,
            authority.UserId, checked(authority.Epoch.Revision + 1), observedAt));
        // This is approval of new text, not a new notice purpose or permission to disclose it.
        return new(ModerationQualificationSourceKind.NoticeDisclosure, request.Intent.ApprovalId, 1,
            MutationValidUntil: deadline);
    }

    private async Task<DateTimeOffset> QualifyCorrectionNoticeAsync(
        ApproveModerationNoticeCorrectionDisclosureSource request, ModerationSourceAuthority authority, CancellationToken token)
    {
        var intent = request.Intent;
        intent.Validate();
        if (intent.ContentRepresentatives.IsDefault || intent.ContentRepresentatives.Length != 0
            || intent.ValidUntil > intent.RetainUntil || clock.GetUtcNow() >= intent.ValidUntil)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await ReadCaseAsync(intent.CaseId, request.Target, intent.ExpectedCaseRevision, authority, token,
            requireOpen: false, requireCurrentAttribution: false);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationDecision>(context, intent.DecisionId, token);
        var decision = await context.ModerationDecisions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == intent.DecisionId, token);
        if (decision is null || decision.CaseId != intent.CaseId || decision.Scope != request.Target
            || decision.Revision != intent.ExpectedDecisionRevision)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        decision.ValidatePersistedState();
        return await ModerationNoticeCorrectionDisclosureQualification.ReadAsync(context, intent,
            request.ExpectedPendingSnapshotHash, request.ExpectedPendingEffectBindingHash, authority, clock, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
    }

    private async Task<DateTimeOffset?> VerifyCorrectionNoticeWriteAsync(
        ApproveModerationNoticeCorrectionDisclosureSource request, ModerationSourceAuthority authority, CancellationToken token)
    {
        var deadline = await QualifyCorrectionNoticeAsync(request, authority, token);
        var approval = await context.ModerationNoticeDisclosureApprovals.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == request.Intent.ApprovalId, token);
        if (approval is null || approval.ApprovedByHumanId != authority.UserId || approval.HostBindingId != authority.Binding.Id
            || approval.TenantId != authority.Tenant.Id || approval.TrustDomainId != authority.Binding.TrustDomainId
            || approval.WriterGeneration != checked(request.ExpectedQualificationRevision + 1)
            || !approval.IsCurrentAt(request.Intent, clock.GetUtcNow()) || clock.GetUtcNow() >= deadline)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        return deadline;
    }

    private async Task RequireCorrectionNoticeAtCommitAsync(
        ApproveModerationNoticeCorrectionDisclosureSource request, ModerationSourceAuthority acquired,
        ModerationQualificationMutationReceipt expected, string digest, IDbContextTransaction transaction, CancellationToken token)
    {
        var owner = new ModerationQualificationAuthority(context, admin, machine);
        var current = await owner.AcquireAsync(request, acquired, token);
        if (current is null || current.UserId != acquired.UserId || current.Binding.Id != acquired.Binding.Id
            || current.Tenant != acquired.Tenant || current.Epoch.Revision != acquired.Epoch.Revision
            || !ReferenceEquals(context.Database.CurrentTransaction, transaction) || context.IsTenantFilterBypassed
            || context.ModerationTrustDomainId != current.Binding.TrustDomainId
            || context.TenantFilterTenantId != current.Tenant.Id || context.ChangeTracker.HasChanges())
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await RequireHumanAcquisitionAsync(request, current, token);
        var receipt = await ReadReceiptAsync(request.OperationId, token);
        if (receipt is null || receipt.SourceId != expected.SourceId || receipt.QualificationRevision != expected.QualificationRevision
            || receipt.CompletedAt != expected.CompletedAt
            || MatchReceipt(receipt, request, digest, current).Outcome != ModerationQualificationWriteOutcome.Known)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var deadline = await VerifyCorrectionNoticeWriteAsync(request, current, token);
        if (!await owner.ProviderTenantMatchesAsync(current.Tenant.Id, token)
            || clock.GetUtcNow() < expected.CompletedAt || clock.GetUtcNow() >= deadline)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
    }
}
