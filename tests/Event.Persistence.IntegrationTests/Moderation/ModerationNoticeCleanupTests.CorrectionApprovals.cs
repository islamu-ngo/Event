using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeCleanupTests
{
    [Test]
    [Arguments(0, false)]
    [Arguments(0, true)]
    [Arguments(1, false)]
    [Arguments(1, true)]
    [Arguments(2, false)]
    [Arguments(2, true)]
    public async Task Correction_text_approval_expires_with_original_even_on_correction_only_page(
        int page, bool revoked)
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var originalApprovalId = Guid.Parse("019a0000-0000-7000-8000-000000000001");
        var correctionApprovalId = Guid.Parse("019a0000-0000-7000-8000-000000000002");
        var originalId = await fixture.SeedAsync(approvalId: originalApprovalId);
        var correctionId = await SeedPendingCleanupCorrectionAsync(fixture, originalId);
        await SeedCorrectionCleanupApprovalAsync(fixture, correctionId, correctionApprovalId, revoked: revoked);
        // An unrelated ordinary approval must remain reachable after the scanned cursor.
        var unrelatedId = await fixture.SeedAsync(
            approvalId: Guid.Parse("019a0000-0000-7000-8000-000000000003"));
        await using var writer = fixture.Context();
        var repository = fixture.Repository(writer, await fixture.AuthenticateAsync(writer));
        var result = await repository.PruneAsync(page == 2 ? originalApprovalId : null, page == 1 ? 2 : 1);
        await Assert.That(result.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Applied);
        await Assert.That(result.LastScannedApprovalId).IsEqualTo(page == 0 ? originalApprovalId : correctionApprovalId);
        await Assert.That(result.Scanned).IsEqualTo(page == 1 ? 2 : 1);
        await Assert.That(result.Purged).IsEqualTo(1);
        await Assert.That(result.OriginalWritesProven).IsEqualTo(1);
        await AssertCorrectionApprovalCleanupStateAsync(fixture, originalId, correctionId, removed: true);
        await using (var reader = fixture.Context())
        {
            reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            await Assert.That(await reader.ModerationNoticeSources.AnyAsync(row => row.EffectId == unrelatedId)).IsTrue();
        }
        var next = await repository.PruneAsync(result.LastScannedApprovalId, 1);
        await Assert.That(next.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Applied);
        await Assert.That(next.Purged).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Instance_custody_projection_does_not_change_correction_writer_authority(bool correctionOnlyPage)
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var originalApprovalId = Guid.Parse("019a0000-0000-7000-8000-000000000001");
        var originalId = await fixture.SeedAsync(approvalId: originalApprovalId, instanceScoped: true);
        var correctionId = await SeedPendingCleanupCorrectionAsync(fixture, originalId);
        await SeedCorrectionCleanupApprovalAsync(fixture, correctionId,
            Guid.Parse("019a0000-0000-7000-8000-000000000002"));
        await using var writer = fixture.Context();
        var result = await fixture.Repository(writer, await fixture.AuthenticateAsync(writer))
            .PruneAsync(correctionOnlyPage ? originalApprovalId : null, 1);
        await Assert.That(result.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Applied);
        await Assert.That(result.Purged).IsEqualTo(1);
        await AssertCorrectionApprovalCleanupStateAsync(fixture, originalId, correctionId, removed: true);
    }

    [Test]
    public async Task Original_only_page_cannot_mistake_rewritten_correction_text_for_independent_notice_authority()
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var originalId = await fixture.SeedAsync(
            approvalId: Guid.Parse("019a0000-0000-7000-8000-000000000001"));
        var correctionId = await SeedPendingCleanupCorrectionAsync(fixture, originalId);
        await SeedCorrectionCleanupApprovalAsync(fixture, correctionId,
            Guid.Parse("019a0000-0000-7000-8000-000000000002"), "rewritten-notice");
        await using var writer = fixture.Context();
        var result = await fixture.Repository(writer, await fixture.AuthenticateAsync(writer)).PruneAsync(null, 1);
        await Assert.That(result.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Unavailable);
        await Assert.That(result.LastScannedApprovalId).IsNull();
        await AssertCorrectionApprovalCleanupStateAsync(fixture, originalId, correctionId, removed: false);
    }

    [Test]
    [Arguments("ordinary-request")]
    [Arguments("digest")]
    [Arguments("snapshot")]
    [Arguments("binding")]
    [Arguments("principal")]
    [Arguments("writer")]
    [Arguments("trust-domain")]
    [Arguments("completed-at")]
    [Arguments("result-revision")]
    [Arguments("ceiling")]
    [Arguments("case")]
    [Arguments("representatives")]
    [Arguments("erased-pending")]
    [Arguments("rewritten-notice")]
    [Arguments("missing-receipt")]
    public async Task Unauthenticated_correction_approval_cannot_be_purged_as_an_original_write(string defect)
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var originalApprovalId = Guid.Parse("019a0000-0000-7000-8000-000000000001");
        var originalId = await fixture.SeedAsync(approvalId: originalApprovalId);
        var correctionId = await SeedPendingCleanupCorrectionAsync(fixture, originalId);
        await SeedCorrectionCleanupApprovalAsync(fixture, correctionId,
            Guid.Parse("019a0000-0000-7000-8000-000000000002"), defect);
        await using var writer = fixture.Context();
        var result = await fixture.Repository(writer, await fixture.AuthenticateAsync(writer))
            .PruneAsync(originalApprovalId, 1);
        await Assert.That(result.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Unavailable);
        await Assert.That(result.LastScannedApprovalId).IsNull();
        await AssertCorrectionApprovalCleanupStateAsync(fixture, originalId, correctionId, removed: false);
    }

    [Test]
    public async Task Expired_correction_text_validity_never_advances_original_retention_ceiling()
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var originalId = await fixture.SeedAsync();
        var correctionId = await SeedPendingCleanupCorrectionAsync(fixture, originalId);
        await SeedCorrectionCleanupApprovalAsync(fixture, correctionId, Guid.CreateVersion7(), revoked: true);
        fixture.Clock.UtcNow = Ceiling.AddTicks(-1);
        await using var writer = fixture.Context();
        var repository = fixture.Repository(writer, await fixture.AuthenticateAsync(writer));
        var live = await repository.PruneAsync(null, 10);
        await Assert.That(live.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Applied);
        await Assert.That(live.Purged).IsEqualTo(0);
        await AssertCorrectionApprovalCleanupStateAsync(fixture, originalId, correctionId, removed: false);
        fixture.Clock.UtcNow = Ceiling;
        var expired = await repository.PruneAsync(null, 10);
        await Assert.That(expired.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Applied);
        await AssertCorrectionApprovalCleanupStateAsync(fixture, originalId, correctionId, removed: true);
    }

    [Test]
    [Arguments("clock")]
    [Arguments("principal")]
    [Arguments("correction-revision")]
    [Arguments("approval-resurrection")]
    public async Task Correction_approval_purge_rechecks_actual_commit_authority_and_absence(string failure)
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var originalId = await fixture.SeedAsync();
        var correctionId = await SeedPendingCleanupCorrectionAsync(fixture, originalId);
        var approvalId = Guid.CreateVersion7();
        await SeedCorrectionCleanupApprovalAsync(fixture, correctionId, approvalId);
        ModerationNoticeDisclosureApproval retainedApproval;
        await using (var reader = fixture.Context())
        {
            reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            retainedApproval = await reader.ModerationNoticeDisclosureApprovals.AsNoTracking()
                .SingleAsync(row => row.Id == approvalId);
        }
        MachinePrincipalAccessor? machine = null;
        var injected = false;
        var observer = new CleanupSaved(async context =>
        {
            if (injected) return;
            injected = true;
            if (failure == "clock") fixture.Clock.UtcNow = Ceiling.AddTicks(-1);
            else if (failure == "principal") machine!.Clear();
            else if (failure == "correction-revision")
                await context.ModerationEffectIntents.Where(row => row.Id == correctionId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Revision, 2L));
            else
            {
                context.Add(retainedApproval);
                await context.SaveChangesAsync();
            }
        });
        await using var writer = fixture.Context(observer);
        machine = await fixture.AuthenticateAsync(writer);
        var result = await fixture.Repository(writer, machine).PruneAsync(null, 10);
        await Assert.That(observer.Observed).IsTrue();
        await Assert.That(result.Outcome == ModerationNoticeCleanupOutcome.Applied).IsFalse();
        await AssertCorrectionApprovalCleanupStateAsync(fixture, originalId, correctionId, removed: false);
    }

    [Test]
    public async Task Correction_approval_does_not_mask_quarantined_native_notification_cleanup_failure()
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var originalId = await fixture.SeedAsync(nativePointer: true);
        var correctionId = await SeedPendingCleanupCorrectionAsync(fixture, originalId);
        await SeedCorrectionCleanupApprovalAsync(fixture, correctionId, Guid.CreateVersion7());
        await using var writer = fixture.Context();
        var result = await fixture.Repository(writer, await fixture.AuthenticateAsync(writer),
            nativeNotificationOwner: true).PruneAsync(null, 10);
        await Assert.That(result.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Unavailable);
        await AssertCorrectionApprovalCleanupStateAsync(fixture, originalId, correctionId, removed: false);
        await using var reader = fixture.Context();
        await Assert.That(await reader.NotificationIntents.IgnoreQueryFilters(["SoftDelete"]).CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task Lost_approved_correction_cleanup_acknowledgement_cannot_complete_revoke_on_retry()
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var originalId = await fixture.SeedAsync();
        var correctionId = await SeedPendingCleanupCorrectionAsync(fixture, originalId);
        await SeedCorrectionCleanupApprovalAsync(fixture, correctionId, Guid.CreateVersion7());
        var fault = new LostCorrectionCleanupAcknowledgement();
        await using (var writer = fixture.Context(fault))
        {
            var result = await fixture.Repository(writer, await fixture.AuthenticateAsync(writer)).PruneAsync(null, 10);
            await Assert.That(result.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Unavailable);
            await Assert.That(result.LastScannedApprovalId).IsNull();
        }
        await Assert.That(fault.Observed).IsTrue();
        await AssertCorrectionApprovalCleanupStateAsync(fixture, originalId, correctionId, removed: true);
        await using var retry = fixture.Context();
        var replay = await fixture.Repository(retry, await fixture.AuthenticateAsync(retry)).PruneAsync(null, 10);
        await Assert.That(replay.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Applied);
        await Assert.That(replay.Purged).IsEqualTo(0);
        await AssertCorrectionApprovalCleanupStateAsync(fixture, originalId, correctionId, removed: true);
    }

    private static async Task SeedCorrectionCleanupApprovalAsync(
        CleanupDatabase fixture, Guid correctionId, Guid approvalId, string? defect = null, bool revoked = false)
    {
        if (defect == "case") await fixture.SeedAsync();
        await using var context = fixture.Context();
        context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var source = await context.ModerationNoticeSources.SingleAsync(row => row.EffectId == correctionId);
        var capture = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson)!;
        var original = await context.ModerationNoticeDisclosureApprovals.SingleAsync(
            row => row.EffectId == capture.PendingCorrection!.OriginalEffectId);
        var decision = await context.ModerationDecisions.SingleAsync(row => row.Id == source.DecisionId);
        var at = OriginalAt.AddMinutes(3);
        var text = new ModerationNoticeSafeText("Separately approved correction text; no body or delivery grant.");
        var intent = original.Intent with
        {
            ApprovalId = approvalId,
            EffectId = correctionId,
            CaseId = defect == "case"
                ? await context.ModerationCases.Where(row => row.Id != original.CaseId).Select(row => row.Id).SingleAsync()
                : original.CaseId,
            RuleMeaning = text,
            ReasonMeaning = text,
            Facts = [text],
            RemainingActions = text,
            Recourse = text,
            Automation = text,
            Retention = text,
            ValidUntil = at.AddMinutes(1),
            RetainUntil = defect == "ceiling" ? Ceiling.AddTicks(1) : Ceiling,
            ContentRepresentatives = defect == "representatives"
                ? [new(Guid.CreateVersion7(), 1, fixture.Database.ReviewerId)] : []
        };
        var approval = ModerationNoticeDisclosureApproval.Approve(intent, fixture.Database.HostId,
            source.TrustDomainId, fixture.Database.TenantId, fixture.Database.AdministratorId, 3, at);
        if (revoked) approval.Revoke(1, fixture.Database.AdministratorId, at.AddSeconds(1));
        var request = new ApproveModerationNoticeCorrectionDisclosureSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = decision.Scope,
            ExpectedQualificationRevision = 2,
            Intent = intent,
            ExpectedPendingSnapshotHash = defect == "snapshot" ? new string('d', 64) : source.SnapshotHash,
            ExpectedPendingEffectBindingHash = defect == "binding" ? new string('e', 64) : source.EffectBindingHash
        };
        ModerationQualificationMutation digested = defect == "ordinary-request"
            ? new ApproveModerationNoticeDisclosureSource
            {
                OperationId = request.OperationId,
                Target = request.Target,
                ExpectedQualificationRevision = request.ExpectedQualificationRevision,
                Intent = intent
            } : request;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(digested.GetType().FullName + "\n"));
        hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(digested, digested.GetType()));
        var digest = Convert.ToHexString(hash.GetHashAndReset());
        context.Add(approval);
        if (defect != "missing-receipt")
            context.Add(ModerationQualificationMutationReceipt.Complete(request.OperationId,
                defect == "digest" ? new string('F', 64) : digest,
                fixture.Database.HostId, defect == "trust-domain" ? Guid.CreateVersion7() : source.TrustDomainId,
                fixture.Database.TenantId, ModerationQualificationSourceKind.NoticeDisclosure,
                approval.Id, defect == "principal" ? Guid.CreateVersion7() : approval.ApprovedByHumanId,
                defect == "result-revision" ? 2 : 1, defect == "writer" ? 4 : approval.WriterGeneration,
                defect == "completed-at" ? at.AddTicks(1) : at));
        await context.SaveChangesAsync();
        if (defect is "erased-pending" or "rewritten-notice")
        {
            var effect = await context.ModerationEffectIntents.SingleAsync(row => row.Id == correctionId);
            var rewritten = capture with { PendingCorrection = null, ValidUntil = null };
            if (defect == "rewritten-notice")
            {
                var originalSource = await context.ModerationNoticeSources.SingleAsync(
                    row => row.EffectId == capture.PendingCorrection!.OriginalEffectId);
                var full = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(originalSource.SnapshotJson)!;
                rewritten = full with
                {
                    Decision = capture.Decision,
                    QualificationRevision = effect.QualificationRevision,
                    Notice = full.Notice! with
                    {
                        EffectId = effect.Id,
                        OperationId = effect.OperationId,
                        OperationKind = effect.OperationKind,
                        SanctionRevision = effect.SanctionRevision,
                        SanctionState = ISLAMU.Moderation.Domain.ModerationSanctionState.Revoked,
                        ApprovalId = approval.Id,
                        CapturedAt = source.CapturedAt
                    },
                    ValidUntil = source.CapturedAt.AddMinutes(1)
                };
            }
            var changed = ModerationNoticeSource.Capture(effect, source.TenantId,
                JsonSerializer.Serialize(rewritten), source.RetainUntil);
            await context.ModerationNoticeSources.Where(row => row.EffectId == correctionId).ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.SnapshotJson, changed.SnapshotJson)
                    .SetProperty(row => row.SnapshotHash, changed.SnapshotHash)
                    .SetProperty(row => row.EffectBindingHash, changed.EffectBindingHash)
                    .SetProperty(row => row.StatusMetadataJson, changed.StatusMetadataJson)
                    .SetProperty(row => row.StatusMetadataHash, changed.StatusMetadataHash));
        }
    }

    private static async Task AssertCorrectionApprovalCleanupStateAsync(
        CleanupDatabase fixture, Guid originalId, Guid correctionId, bool removed)
    {
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        foreach (var effectId in new[] { originalId, correctionId })
        {
            await Assert.That(await reader.ModerationNoticeSources.AnyAsync(row => row.EffectId == effectId)).IsEqualTo(!removed);
            await Assert.That(await reader.ModerationNotices.CountAsync(row => row.EffectId == effectId)).IsEqualTo(removed ? 0 : 2);
            await Assert.That(await reader.ModerationNoticeDisclosureApprovals.AnyAsync(row => row.EffectId == effectId)).IsEqualTo(!removed);
        }
        var correction = await reader.ModerationEffectIntents.SingleAsync(row => row.Id == correctionId);
        await Assert.That(correction.CompletedAt).IsNull();
        await Assert.That(correction.OriginalCompletionId).IsNull();
        await Assert.That(correction.OriginalNoticeCleanupId).IsNull();
        await Assert.That(correction.Revision).IsEqualTo(1L);
        await Assert.That((await reader.ModerationEffectIntents.SingleAsync(row => row.Id == originalId))
            .HasOriginalNoticeCleanup(fixture.Database.HostId, fixture.Database.TenantId, Ceiling)).IsEqualTo(removed);
    }
}
