using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    private async Task<DateTimeOffset?> VerifyContentNoticeWriteAsync(
        ModerationQualificationMutation request, ModerationSourceAuthority authority, CancellationToken token)
    {
        if (request is ApproveModerationNoticeCorrectionDisclosureSource correction)
            return await VerifyCorrectionNoticeWriteAsync(correction, authority, token);
        if (request is not ApproveModerationNoticeDisclosureSource source) return null;
        var decision = await context.ModerationDecisions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == source.Intent.DecisionId, token);
        if (decision is null || decision.Scope != source.Target
            || decision.SubjectKind == ModerationSubjectKind.Content && authority.DisclosureAuthority is null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var deadline = await ModerationNoticeContentCustody.AcquireAsync(context, privacy, decision.Subject,
            authority.Tenant, source.Intent.ContentRepresentatives, authority.FencedHumans,
            clock.GetUtcNow(), source.Intent.ValidUntil, token, authority.DisclosureAuthority);
        if (deadline is null || clock.GetUtcNow() >= deadline)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        return deadline;
    }

    private async Task RequireContentNoticeAtCommitAsync(
        ApproveModerationNoticeDisclosureSource request, ModerationSourceAuthority acquired,
        ModerationQualificationMutationReceipt expected, string digest,
        IDbContextTransaction transaction, CancellationToken token)
    {
        var authority = new ModerationQualificationAuthority(context, admin, machine);
        var current = await authority.AcquireAsync(request, acquired, token);
        if (current is null || current.UserId != acquired.UserId || current.Binding.Id != acquired.Binding.Id
            || current.Tenant != acquired.Tenant || current.Epoch.Revision != acquired.Epoch.Revision
            || !ReferenceEquals(context.Database.CurrentTransaction, transaction)
            || context.ModerationTrustDomainId != current.Binding.TrustDomainId
            || context.TenantFilterTenantId != current.Tenant.Id || context.IsTenantFilterBypassed
            || context.ChangeTracker.HasChanges())
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await RequireHumanAcquisitionAsync(request, current, token);
        var receipt = await ReadReceiptAsync(request.OperationId, token);
        if (receipt is null || receipt.HostBindingId != expected.HostBindingId
            || receipt.TrustDomainId != expected.TrustDomainId || receipt.TenantId != expected.TenantId
            || receipt.SourceKind != expected.SourceKind || receipt.SourceId != expected.SourceId
            || receipt.PrincipalId != expected.PrincipalId || receipt.ResultRevision != expected.ResultRevision
            || receipt.QualificationRevision != expected.QualificationRevision || receipt.CompletedAt != expected.CompletedAt
            || MatchReceipt(receipt, request, digest, current).Outcome != ModerationQualificationWriteOutcome.Known)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var approval = await context.ModerationNoticeDisclosureApprovals.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == receipt.SourceId, token);
        if (approval is null || approval.Revision != receipt.ResultRevision
            || !approval.IsCurrentAt(request.Intent, clock.GetUtcNow()))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var deadline = await VerifyContentNoticeWriteAsync(request, current, token);
        if (!await authority.ProviderTenantMatchesAsync(current.Tenant.Id, token)
            || clock.GetUtcNow() < expected.CompletedAt || deadline is null || clock.GetUtcNow() >= deadline)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
    }
}
