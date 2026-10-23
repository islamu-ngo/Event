using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeReadTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Native_correction_approval_adds_only_approval_receipt_and_epoch_without_resolving_obligations(bool colocated)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var pending = await PrepareCorrectionApprovalAsync(fixture);
        await using var primary = fixture.Database.Context();
        await using var privacy = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(primary) : null;
        var before = await CorrectionApprovalStateAsync(fixture, pending);

        var result = await WriteCorrectionApprovalAsync(fixture, pending.Request, privacy);

        await Assert.That(result.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(result.Receipt).IsNotNull();
        await Assert.That(result.Receipt!.Id).IsEqualTo(pending.Request.OperationId);
        await Assert.That(result.Receipt.SourceId).IsEqualTo(pending.Request.Intent.ApprovalId);
        await Assert.That(result.Receipt.SourceKind).IsEqualTo(ModerationQualificationSourceKind.NoticeDisclosure);
        await Assert.That(result.Receipt.PrincipalId).IsEqualTo(fixture.Database.AdministratorId);
        await Assert.That(result.Receipt.QualificationRevision).IsEqualTo(pending.Request.ExpectedQualificationRevision + 1);
        await using (var inspect = fixture.Database.Context())
        {
            inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            var approval = await inspect.ModerationNoticeDisclosureApprovals.AsNoTracking()
                .SingleAsync(row => row.Id == pending.Request.Intent.ApprovalId);
            await Assert.That(JsonSerializer.Serialize(approval.Intent)).IsEqualTo(JsonSerializer.Serialize(pending.Request.Intent));
            await Assert.That(approval.RetainUntil).IsEqualTo(pending.Original.Source.RetainUntil!.Value);
            await Assert.That(approval.ValidUntil <= approval.RetainUntil).IsTrue();
        }
        await Assert.That(await CorrectionApprovalStateAsync(fixture, pending)).IsEqualTo(before with
        {
            Approvals = before.Approvals + 1,
            QualificationReceipts = before.QualificationReceipts + 1,
            QualificationRevision = before.QualificationRevision + 1
        });
        await AssertCorrectionApprovalHasNoDeliveryAsync(fixture, pending);
    }

    [Test]
    public async Task Native_correction_approval_can_use_the_last_original_custody_window_without_restarting_contestation()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var pending = await PrepareCorrectionApprovalAsync(fixture);
        var originalUntil = pending.Original.Source.RetainUntil!.Value;
        fixture.Clock.UtcNow = originalUntil.AddMinutes(-1);
        var request = pending.Request with { Intent = pending.Request.Intent with { ValidUntil = originalUntil } };
        var before = await CorrectionApprovalStateAsync(fixture, pending);

        var result = await WriteCorrectionApprovalAsync(fixture, request);

        await Assert.That(result.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(await CorrectionApprovalStateAsync(fixture, pending)).IsEqualTo(before with
        {
            Approvals = before.Approvals + 1,
            QualificationReceipts = before.QualificationReceipts + 1,
            QualificationRevision = before.QualificationRevision + 1
        });
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var approval = await inspect.ModerationNoticeDisclosureApprovals.AsNoTracking()
            .SingleAsync(row => row.Id == request.Intent.ApprovalId);
        await Assert.That(approval.ValidUntil).IsEqualTo(originalUntil);
        await Assert.That(approval.RetainUntil).IsEqualTo(originalUntil);
    }

    [Test]
    public async Task Native_correction_approval_replay_is_exact_and_changed_safe_text_cannot_reuse_its_receipt()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var pending = await PrepareCorrectionApprovalAsync(fixture);
        var first = await WriteCorrectionApprovalAsync(fixture, pending.Request);
        await Assert.That(first.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var accepted = await CorrectionApprovalStateAsync(fixture, pending);

        var replay = await WriteCorrectionApprovalAsync(fixture, pending.Request);
        await Assert.That(replay.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(replay.Receipt!.Id).IsEqualTo(first.Receipt!.Id);
        await Assert.That(replay.Receipt.QualificationRevision).IsEqualTo(first.Receipt.QualificationRevision);
        var changed = pending.Request with
        {
            Intent = pending.Request.Intent with { RemainingActions = new("Substituted text was never approved.") }
        };
        await Assert.That((await WriteCorrectionApprovalAsync(fixture, changed)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Conflict);
        await Assert.That(await CorrectionApprovalStateAsync(fixture, pending)).IsEqualTo(accepted);
        await AssertCorrectionApprovalHasNoDeliveryAsync(fixture, pending);
    }

    [Test]
    [Arguments("snapshotHash")]
    [Arguments("bindingHash")]
    [Arguments("recipient")]
    [Arguments("extendedRetention")]
    [Arguments("shortenedRetention")]
    [Arguments("validityBeyondRetention")]
    [Arguments("expiredRetention")]
    [Arguments("administrator")]
    [Arguments("publication")]
    [Arguments("profile")]
    [Arguments("contribution")]
    [Arguments("selectionDigest")]
    [Arguments("currentPolicy")]
    [Arguments("missingOriginal")]
    [Arguments("originalSnapshotHash")]
    [Arguments("originalBindingHash")]
    public async Task Native_correction_approval_rejects_forged_pending_identity_original_metadata_and_custody(string drift)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var pending = await PrepareCorrectionApprovalAsync(fixture);
        var request = pending.Request;
        switch (drift)
        {
            case "snapshotHash": request = request with { ExpectedPendingSnapshotHash = new string('0', 64) }; break;
            case "bindingHash": request = request with { ExpectedPendingEffectBindingHash = new string('0', 64) }; break;
            case "recipient":
                await fixture.MutateAsync(async context => await context.ModerationNotices
                    .Where(row => row.EffectId == pending.Effect.Id && row.Audience == ModerationNoticeAudience.Reviewer)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.RecipientSubjectId, fixture.Database.UrgencyAssessorId)));
                break;
            case "extendedRetention": request = request with { Intent = request.Intent with { RetainUntil = request.Intent.RetainUntil.AddTicks(10) } }; break;
            case "shortenedRetention": request = request with { Intent = request.Intent with { RetainUntil = request.Intent.RetainUntil.AddTicks(-10) } }; break;
            case "validityBeyondRetention": request = request with { Intent = request.Intent with { ValidUntil = request.Intent.RetainUntil.AddTicks(10) } }; break;
            case "expiredRetention": fixture.Clock.UtcNow = request.Intent.RetainUntil; break;
            case "administrator":
                await fixture.MutateAsync(async context => await context.PlatformUserRoles
                    .Where(row => row.UserId == fixture.Database.AdministratorId).ExecuteDeleteAsync());
                break;
            case "publication": request = request with { Intent = request.Intent with { PublicationId = Guid.CreateVersion7() } }; break;
            case "profile": request = request with { Intent = request.Intent with { ProfileId = Guid.CreateVersion7() } }; break;
            case "contribution": request = request with { Intent = request.Intent with { ContributionId = Guid.CreateVersion7() } }; break;
            case "selectionDigest": request = request with { Intent = request.Intent with { SelectionDigest = new string('0', 64) } }; break;
            case "currentPolicy":
                await fixture.MutateAsync(async context => await context.ModerationPolicySelections
                    .Where(row => row.Id == request.Intent.PolicyId).ExecuteDeleteAsync());
                break;
            case "missingOriginal":
                await fixture.MutateAsync(async context => await context.ModerationNoticeSources
                    .Where(row => row.EffectId == pending.Original.Effect.Id).ExecuteDeleteAsync());
                break;
            case "originalSnapshotHash":
                await fixture.MutateAsync(async context => await context.ModerationNoticeSources
                    .Where(row => row.EffectId == pending.Original.Effect.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.SnapshotHash, new string('0', 64))));
                break;
            case "originalBindingHash":
                await fixture.MutateAsync(async context => await context.ModerationNoticeSources
                    .Where(row => row.EffectId == pending.Original.Effect.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.EffectBindingHash, new string('0', 64))));
                break;
        }
        var before = await CorrectionApprovalStateAsync(fixture, pending);

        var result = await WriteCorrectionApprovalAsync(fixture, request);

        await Assert.That(result.Outcome == ModerationQualificationWriteOutcome.Known).IsFalse();
        await Assert.That(result.Receipt).IsNull();
        await Assert.That(await CorrectionApprovalStateAsync(fixture, pending)).IsEqualTo(before);
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await inspect.ModerationNoticeDisclosureApprovals.AnyAsync(row => row.Id == request.Intent.ApprovalId)).IsFalse();
        await Assert.That(await inspect.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == request.OperationId)).IsFalse();
    }

    [Test]
    [Arguments("validity", false)]
    [Arguments("validity", true)]
    [Arguments("retention", false)]
    [Arguments("retention", true)]
    [Arguments("administrator", false)]
    [Arguments("administrator", true)]
    public async Task Native_correction_approval_rolls_back_its_flushed_prefix_when_time_or_administrator_authority_is_lost(
        string boundary, bool colocated)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var pending = await PrepareCorrectionApprovalAsync(fixture);
        await using var primary = fixture.Database.Context();
        await using var privacy = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(primary) : null;
        var before = await CorrectionApprovalStateAsync(fixture, pending);
        var probe = new InvalidateCorrectionApprovalAfterSave(fixture, pending.Request, boundary);

        var result = await WriteCorrectionApprovalAsync(fixture, pending.Request, privacy, probe);

        await Assert.That(probe.Observed).IsTrue();
        await Assert.That(result.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
        await Assert.That(result.Receipt).IsNull();
        await Assert.That(await CorrectionApprovalStateAsync(fixture, pending)).IsEqualTo(before);
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await inspect.ModerationNoticeDisclosureApprovals.AnyAsync(row => row.Id == pending.Request.Intent.ApprovalId)).IsFalse();
        await Assert.That(await inspect.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == pending.Request.OperationId)).IsFalse();
        await Assert.That(await inspect.PlatformUserRoles.AnyAsync(row => row.UserId == fixture.Database.AdministratorId)).IsTrue();
    }

    [Test]
    public async Task Ordinary_future_effect_disclosure_approval_still_rejects_an_existing_pending_revoke_effect()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var pending = await PrepareCorrectionApprovalAsync(fixture);
        var ordinary = new ApproveModerationNoticeDisclosureSource
        {
            OperationId = pending.Request.OperationId, Target = pending.Request.Target,
            ExpectedQualificationRevision = pending.Request.ExpectedQualificationRevision, Intent = pending.Request.Intent
        };
        var before = await CorrectionApprovalStateAsync(fixture, pending);

        var result = await WriteCorrectionApprovalAsync(fixture, ordinary);

        await Assert.That(result.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Conflict);
        await Assert.That(result.Receipt).IsNull();
        await Assert.That(await CorrectionApprovalStateAsync(fixture, pending)).IsEqualTo(before);
    }

    private sealed class InvalidateCorrectionApprovalAfterSave(NoticeDatabase fixture,
        ApproveModerationNoticeCorrectionDisclosureSource request, string boundary) : SaveChangesInterceptor
    {
        public bool Observed { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!Observed && eventData.Context is ExploreDbContext context
                && context.ModerationNoticeDisclosureApprovals.Local.Any(row => row.Id == request.Intent.ApprovalId)
                && context.ModerationQualificationMutationReceipts.Local.Any(row => row.Id == request.OperationId))
            {
                Observed = true;
                if (boundary == "administrator")
                    await context.PlatformUserRoles.Where(row => row.UserId == fixture.Database.AdministratorId)
                        .ExecuteDeleteAsync(cancellationToken);
                else
                    fixture.Clock.UtcNow = boundary == "retention" ? request.Intent.RetainUntil : request.Intent.ValidUntil;
            }
            return result;
        }
    }

}
