using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    public async Task Correction_approval_proof_retains_attributed_tenant_case_for_exact_child_resource_decision()
    {
        await using var fixture = await OriginalCaseScopeFixture.CreateAsync();
        Guid correctionId;
        Guid approvalId;
        await using (var writer = fixture.Context())
        {
            writer.ModerationTrustDomainId = fixture.Original.Case.Scope.TrustDomainId;
            await using var transaction = await writer.Database.BeginTransactionAsync();
            var owner = await writer.ModerationCases.SingleAsync(row => row.Id == fixture.Original.Case.Id);
            var decision = await writer.ModerationDecisions.SingleAsync(row => row.CaseId == owner.Id);
            var originalEffect = await writer.ModerationEffectIntents.SingleAsync(row => row.DecisionId == decision.Id);
            var originalSource = await writer.ModerationNoticeSources.SingleAsync(row => row.EffectId == originalEffect.Id);
            var original = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(originalSource.SnapshotJson)
                ?? throw new InvalidOperationException("The native original capture is required.");
            var originalR = originalSource.RetainUntil
                ?? throw new InvalidOperationException("The native original retention ceiling is required.");
            var originalApproval = await writer.ModerationNoticeDisclosureApprovals
                .SingleAsync(row => row.EffectId == originalEffect.Id);
            var attribution = await writer.ModerationCaseAttributionSources
                .SingleAsync(row => row.Id == fixture.Finding.AttributionSourceId);
            var historicalRead = await writer.ModerationCaseReadGrants.SingleAsync(row => row.Id == attribution.ReadGrantId);
            await Assert.That(owner.Scope.Kind).IsEqualTo(ModerationScopeKind.Tenant);
            await Assert.That(decision.Scope).IsEqualTo(AuthorityScope.Resource(owner.Scope, fixture.Database.ResourceId));
            await Assert.That(decision.Scope.Parent).IsEqualTo(owner.Scope);
            await Assert.That(owner.Subject).IsEqualTo(decision.Subject);
            await Assert.That(attribution.CaseId).IsEqualTo(owner.Id);
            await Assert.That(attribution.Generation).IsEqualTo(fixture.Finding.AttributionGeneration);
            await Assert.That(historicalRead.IsCurrentAt(originalR)).IsFalse();
            await Assert.That(original.Matches(originalEffect)).IsTrue();
            await Assert.That(original.ContentRepresentatives.IsEmpty).IsFalse();

            var originals = new List<ModerationNotice>
            {
                ModerationNotice.Create(Guid.CreateVersion7(), originalSource, ModerationNoticeAudience.Subject,
                    originalEffect.SubjectKind, originalEffect.SubjectId, null, ModerationNoticeDeliveryDisposition.NoHumanProjection)
            };
            originals.AddRange(original.Reviewers.Select(row => ModerationNotice.Create(Guid.CreateVersion7(),
                originalSource, ModerationNoticeAudience.Reviewer, (int)ModerationSubjectKind.Human,
                row.ReviewerHumanId, null, ModerationNoticeDeliveryDisposition.NativeRoutingUnavailable)));
            originals.AddRange(original.ContentRepresentatives.Select(row => ModerationNotice.Create(Guid.CreateVersion7(),
                originalSource, ModerationNoticeAudience.ContentRepresentative, (int)ModerationSubjectKind.Human,
                row.HumanId, null, ModerationNoticeDeliveryDisposition.NativeRoutingUnavailable)));

            var at = originalSource.CapturedAt.AddMinutes(2);
            var sanction = await writer.ModerationSanctions.SingleAsync(row => row.Id == originalEffect.SanctionId);
            sanction.Revoke(sanction.Revision, at);
            var epoch = await writer.ModerationQualificationEpochs.SingleAsync();
            var correction = ModerationEffectIntent.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), decision.Id,
                originalEffect.DecisionRevision, sanction.Id, sanction.Revision, originalEffect.TrustDomainId,
                originalEffect.ScopeKind, originalEffect.ScopeId, originalEffect.SubjectKind, originalEffect.SubjectId,
                (int)ModerationOperationKind.Revoke, originalEffect.PolicyRevision, epoch.Revision, at, null, null);
            correctionId = correction.Id;
            var pending = new ModerationNoticeCorrectionObligations(originalEffect.Id, originalSource.SnapshotHash,
                originalSource.EffectBindingHash, originalR, originals.Select(row => new ModerationNoticeCorrectionRecipient(
                    row.Id, row.Audience, row.RecipientSubjectKind, row.RecipientSubjectId,
                    ModerationNoticeDeliveryDisposition.RecipientUnavailable)).ToImmutableArray());
            var capture = new ModerationNoticeCaptureSnapshot(2, "event.moderation.capture.v2", original.HostBindingId,
                original.HostSolutionId, original.HostInstanceId, original.WriterTenantId, original.AuthoritySourceId,
                correction.QualificationRevision, original.Decision, null, null, [], originalR)
            { PendingCorrection = pending };
            await Assert.That(capture.Matches(correction)).IsTrue();
            var source = ModerationNoticeSource.Capture(correction, fixture.Database.TenantId,
                JsonSerializer.Serialize(capture), originalR);
            var text = new ModerationNoticeSafeText("Separately approved correction text; no body or delivery grant.");
            var intent = originalApproval.Intent with
            {
                ApprovalId = Guid.CreateVersion7(),
                EffectId = correction.Id,
                RuleMeaning = text,
                ReasonMeaning = text,
                Facts = [text],
                RemainingActions = text,
                Recourse = text,
                Automation = text,
                Retention = text,
                ContentRepresentatives = [],
                ValidUntil = at.AddMinutes(1),
                RetainUntil = originalR
            };
            var request = new ApproveModerationNoticeCorrectionDisclosureSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = decision.Scope,
                ExpectedQualificationRevision = epoch.Revision,
                Intent = intent,
                ExpectedPendingSnapshotHash = source.SnapshotHash,
                ExpectedPendingEffectBindingHash = source.EffectBindingHash
            };
            epoch.Advance(epoch.Revision);
            var approval = ModerationNoticeDisclosureApproval.Approve(intent, original.HostBindingId,
                source.TrustDomainId, fixture.Database.TenantId, originalApproval.ApprovedByHumanId, epoch.Revision, at);
            approvalId = approval.Id;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(Encoding.UTF8.GetBytes(request.GetType().FullName + "\n"));
            hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(request, request.GetType()));
            var receipt = ModerationQualificationMutationReceipt.Complete(request.OperationId,
                Convert.ToHexString(hash.GetHashAndReset()), approval.HostBindingId, approval.TrustDomainId,
                approval.TenantId, ModerationQualificationSourceKind.NoticeDisclosure, approval.Id,
                approval.ApprovedByHumanId, 1, approval.WriterGeneration, at);
            writer.AddRange(correction, source, approval, receipt);
            writer.AddRange(originals);
            writer.AddRange(pending.Recipients.Select(row => ModerationNotice.CreatePendingCorrection(Guid.CreateVersion7(),
                source, row.Audience, row.RecipientSubjectKind, row.RecipientSubjectId)));
            await writer.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Original.Case.Scope.TrustDomainId;
        var retainedSource = await reader.ModerationNoticeSources.AsNoTracking().SingleAsync(row => row.EffectId == correctionId);
        var retainedApproval = await reader.ModerationNoticeDisclosureApprovals.AsNoTracking().SingleAsync(row => row.Id == approvalId);
        var retainedCapture = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(retainedSource.SnapshotJson)
            ?? throw new InvalidOperationException("The pending correction capture is required.");
        var retainedPending = retainedCapture.PendingCorrection
            ?? throw new InvalidOperationException("The original correction obligations must remain pending.");
        await Assert.That(await ModerationNoticeCorrectionApprovalProof.MatchesAsync(
            reader, retainedApproval, retainedSource, retainedCapture, CancellationToken.None)).IsTrue();
        var retainedEffect = await reader.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == correctionId);
        await Assert.That(retainedEffect.CompletedAt).IsNull();
        await Assert.That(retainedEffect.OriginalCompletionId).IsNull();
        await Assert.That(retainedEffect.OriginalNoticeCleanupId).IsNull();
        await Assert.That(await reader.ModerationOriginalEffectCompletionReceipts.AnyAsync(row => row.EffectId == correctionId)).IsFalse();
        await Assert.That(retainedSource.RetainUntil).IsEqualTo(retainedPending.RetainUntil);
        await Assert.That(retainedApproval.RetainUntil).IsEqualTo(retainedSource.RetainUntil);
    }
}
