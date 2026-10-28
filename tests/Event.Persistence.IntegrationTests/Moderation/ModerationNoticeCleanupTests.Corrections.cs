using System.Collections.Immutable;
using System.Data.Common;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeCleanupTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Original_ceiling_removes_pending_correction_and_original_payload_without_completing_revoke(
        bool instanceScoped, bool legacyNullableCorrection)
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var originalId = await fixture.SeedAsync(acknowledged: true, instanceScoped: instanceScoped);
        var correctionId = await SeedPendingCleanupCorrectionAsync(fixture, originalId,
            legacyNullableProjection: legacyNullableCorrection);
        await using (var inspect = fixture.Context())
        {
            inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            var sources = await inspect.ModerationNoticeSources.OrderBy(row => row.EffectId).ToArrayAsync();
            Guid? originalTenant = instanceScoped ? null : fixture.Database.TenantId;
            Guid? correctionTenant = legacyNullableCorrection ? null : fixture.Database.TenantId;
            await Assert.That(sources.Length).IsEqualTo(2);
            await Assert.That(sources.Single(row => row.EffectId == originalId).TenantId).IsEqualTo(originalTenant);
            await Assert.That(sources.Single(row => row.EffectId == correctionId).TenantId).IsEqualTo(correctionTenant);
            await Assert.That(sources.All(row => row.ScopeKind == (int)(instanceScoped
                    ? fixture.Database.Instance.Kind : fixture.Database.Scope.Kind)
                && row.ScopeId == (instanceScoped ? fixture.Database.Instance.Id : fixture.Database.Scope.Id))).IsTrue();
            await Assert.That(sources.All(row => JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(row.SnapshotJson)!
                .WriterTenantId == fixture.Database.TenantId)).IsTrue();
        }
        await using var writer = fixture.Context();
        var result = await fixture.Repository(writer, await fixture.AuthenticateAsync(writer),
            nativeNotificationOwner: true).PruneAsync(null, 10);
        await Assert.That(result.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Applied);
        await Assert.That(result.Purged).IsEqualTo(1);
        await Assert.That(result.OriginalWritesProven).IsEqualTo(1);
        await Assert.That(writer.Database.CurrentTransaction).IsNull();
        await Assert.That(writer.ModerationTrustDomainId).IsNull();
        await Assert.That(writer.ChangeTracker.Entries().Any()).IsFalse();
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationNoticeSources.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationNotices.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationNoticeDisclosureApprovals.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationNoticeNotificationReceipts.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationCases.CountAsync()).IsEqualTo(1);
        await Assert.That(await reader.ModerationFindings.CountAsync()).IsEqualTo(1);
        await Assert.That(await reader.ModerationDecisions.CountAsync()).IsEqualTo(1);
        await Assert.That((await reader.ModerationSanctions.SingleAsync()).State).IsEqualTo(ModerationSanctionState.Revoked);
        await Assert.That(await reader.ModerationOperationReceipts.CountAsync()).IsEqualTo(2);
        var original = await reader.ModerationEffectIntents.SingleAsync(row => row.Id == originalId);
        await Assert.That(original.HasOriginalNoticeCleanup(fixture.Database.HostId, fixture.Database.TenantId, Ceiling)).IsTrue();
        var correction = await reader.ModerationEffectIntents.SingleAsync(row => row.Id == correctionId);
        await Assert.That(correction.CompletedAt).IsNull();
        await Assert.That(correction.LeaseId).IsNull();
        await Assert.That(correction.OriginalNoticeCleanupId).IsNull();
        await Assert.That(correction.OriginalCompletionId).IsNull();
        await Assert.That(correction.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task Existing_native_cleanup_limitation_cannot_report_pending_correction_purged()
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var originalId = await fixture.SeedAsync(nativePointer: true);
        var correctionId = await SeedPendingCleanupCorrectionAsync(fixture, originalId);
        var occurrences = await ReadCleanupOccurrencesAsync(fixture);
        await using var writer = fixture.Context();
        var result = await fixture.Repository(writer, await fixture.AuthenticateAsync(writer),
            nativeNotificationOwner: true).PruneAsync(null, 10);
        await Assert.That(result.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Unavailable);
        await AssertPendingCleanupIntactAsync(fixture, originalId, correctionId, occurrences);
        await using var inspect = fixture.Context();
        await Assert.That(await inspect.NotificationIntents.IgnoreQueryFilters(["SoftDelete"]).CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Earlier_independent_revoke_cleanup_allows_creating_cleanup_only_without_residual_native_copies(bool residualNativeCopy)
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var originalCeiling = Ceiling.AddHours(1);
        var originalId = await fixture.SeedAsync(retainUntil: originalCeiling);
        var (revokeId, revokeNoticeId, revokeHash) = await SeedApprovedCleanupRevokeAsync(fixture, originalId);
        await using (var writer = fixture.Context())
        {
            var earlier = await fixture.Repository(writer, await fixture.AuthenticateAsync(writer)).PruneAsync(null, 10);
            await Assert.That(earlier.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Applied);
            await Assert.That(earlier.Purged).IsEqualTo(1);
            await Assert.That(earlier.OriginalWritesProven).IsEqualTo(1);
        }
        await using (var inspect = fixture.Context())
        {
            inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            var revoke = await inspect.ModerationEffectIntents.SingleAsync(row => row.Id == revokeId);
            await Assert.That(revoke.HasOriginalNoticeCleanup(fixture.Database.HostId, fixture.Database.TenantId, Ceiling)).IsTrue();
            await Assert.That(revoke.CompletedAt).IsNull();
            await Assert.That(await inspect.ModerationNoticeSources.AnyAsync(row => row.EffectId == revokeId)).IsFalse();
            await Assert.That(await inspect.ModerationNotices.AnyAsync(row => row.EffectId == revokeId)).IsFalse();
            await Assert.That(await inspect.ModerationNoticeDisclosureApprovals.AnyAsync(row => row.EffectId == revokeId)).IsFalse();
            await Assert.That(await inspect.ModerationNoticeNotificationReceipts.AnyAsync(row => row.EffectId == revokeId)).IsFalse();
            await Assert.That(await inspect.Set<ModerationContentResponse>().AnyAsync(row => row.EffectId == revokeId)).IsFalse();
            if (residualNativeCopy)
            {
                inspect.NotificationIntents.Add(new NotificationIntent
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = fixture.Database.TenantId,
                    CategoryId = (int)NotificationCategoryEnum.TrustSafetyModeration,
                    OwnershipTypeId = (int)NotificationOwnershipTypeEnum.IslamuEvent,
                    RecipientKindId = (int)NotificationRecipientKindEnum.User,
                    StatusId = (int)NotificationIntentStatusEnum.Pending,
                    RecipientUserId = fixture.Database.SubjectId,
                    TemplateKey = "moderation.notice.portal.v1",
                    DeduplicationKey = $"moderation-notice:{revokeId:N}:1:{fixture.Database.SubjectId:N}",
                    SafePayloadReference = $"moderation-notice:{revokeNoticeId:D}",
                    SafePayloadHash = revokeHash,
                    CreatedAt = OriginalAt.AddMinutes(2).UtcDateTime,
                    IsDeleted = true
                });
                await inspect.SaveChangesAsync();
            }
        }
        fixture.Clock.UtcNow = originalCeiling;
        await using var finalWriter = fixture.Context();
        var result = await fixture.Repository(finalWriter, await fixture.AuthenticateAsync(finalWriter)).PruneAsync(null, 10);
        await Assert.That(result.Outcome).IsEqualTo(residualNativeCopy
            ? ModerationNoticeCleanupOutcome.Unavailable : ModerationNoticeCleanupOutcome.Applied);
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationNoticeSources.CountAsync()).IsEqualTo(residualNativeCopy ? 1 : 0);
        await Assert.That(await reader.ModerationNotices.CountAsync()).IsEqualTo(residualNativeCopy ? 2 : 0);
        await Assert.That(await reader.ModerationNoticeDisclosureApprovals.CountAsync()).IsEqualTo(residualNativeCopy ? 1 : 0);
        await Assert.That(await reader.NotificationIntents.IgnoreQueryFilters(["SoftDelete"]).CountAsync())
            .IsEqualTo(residualNativeCopy ? 1 : 0);
        var original = await reader.ModerationEffectIntents.SingleAsync(row => row.Id == originalId);
        await Assert.That(original.HasOriginalNoticeCleanup(fixture.Database.HostId, fixture.Database.TenantId, originalCeiling))
            .IsEqualTo(!residualNativeCopy);
        await Assert.That(await reader.ModerationEffectIntents.CountAsync()).IsEqualTo(2);
        await Assert.That(await reader.ModerationEffectIntents.AnyAsync(row => row.CompletedAt != null)).IsFalse();
        await Assert.That(await reader.ModerationCases.CountAsync()).IsEqualTo(1);
        await Assert.That(await reader.ModerationFindings.CountAsync()).IsEqualTo(1);
        await Assert.That(await reader.ModerationDecisions.CountAsync()).IsEqualTo(1);
        await Assert.That((await reader.ModerationSanctions.SingleAsync()).State).IsEqualTo(ModerationSanctionState.Revoked);
        await Assert.That(await reader.ModerationOperationReceipts.CountAsync()).IsEqualTo(2);
    }

    [Test]
    public async Task Missing_pending_source_never_proves_prior_original_cleanup()
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var originalId = await fixture.SeedAsync();
        var correctionId = await SeedPendingCleanupCorrectionAsync(fixture, originalId);
        var originalOccurrences = (await ReadCleanupOccurrencesAsync(fixture))
            .Where(row => row.EffectId == originalId).Select(row => row.Id).ToArray();
        await using (var tamper = fixture.Context())
        {
            tamper.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            await tamper.ModerationNotices.Where(row => row.EffectId == correctionId).ExecuteDeleteAsync();
            await tamper.ModerationNoticeSources.Where(row => row.EffectId == correctionId).ExecuteDeleteAsync();
        }
        await using var writer = fixture.Context();
        var result = await fixture.Repository(writer, await fixture.AuthenticateAsync(writer)).PruneAsync(null, 10);
        await Assert.That(result.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Unavailable);
        await Assert.That(result.LastScannedApprovalId).IsNull();
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationNoticeSources.AnyAsync(row => row.EffectId == originalId)).IsTrue();
        await Assert.That(await reader.ModerationNoticeDisclosureApprovals.CountAsync()).IsEqualTo(1);
        await Assert.That((await reader.ModerationNotices.Where(row => row.EffectId == originalId)
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync()).SequenceEqual(originalOccurrences)).IsTrue();
        await Assert.That(await reader.ModerationNotices.AnyAsync(row => row.EffectId == correctionId)).IsFalse();
        await Assert.That(await reader.ModerationEffectIntents.AnyAsync(row => row.OriginalNoticeCleanupId != null
            || row.CompletedAt != null)).IsFalse();
    }

    [Test]
    public async Task Independent_same_ceiling_notices_cannot_starve_original_correction_cleanup()
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var first = Guid.Parse("019a0000-0000-7000-8000-000000000001");
        var originalId = await fixture.SeedAsync(approvalId: first);
        var correctionId = await SeedPendingCleanupCorrectionAsync(fixture, originalId);
        for (var index = 0; index < 1025; index++) await fixture.SeedAsync();
        await using var writer = fixture.Context();
        var result = await fixture.Repository(writer, await fixture.AuthenticateAsync(writer)).PruneAsync(null, 1);
        await Assert.That(result.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Applied);
        await Assert.That(result.LastScannedApprovalId).IsEqualTo(first);
        await Assert.That(result.Purged).IsEqualTo(1);
        await using var inspect = fixture.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await inspect.ModerationNoticeSources.CountAsync()).IsEqualTo(1025);
        await Assert.That(await inspect.ModerationNoticeSources.AnyAsync(row => row.EffectId == correctionId)).IsFalse();
        await Assert.That(await inspect.ModerationNoticeDisclosureApprovals.CountAsync()).IsEqualTo(1025);
    }

    [Test]
    public async Task Pending_correction_inherits_exact_ceiling_and_does_not_capture_independent_normal_notice()
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var firstApproval = Guid.Parse("019a0000-0000-7000-8000-000000000001");
        var originalId = await fixture.SeedAsync(approvalId: firstApproval);
        var independentId = await fixture.SeedAsync(approvalId: Guid.Parse("019a0000-0000-7000-8000-000000000002"));
        var correctionId = await SeedPendingCleanupCorrectionAsync(fixture, originalId);
        var independentCorrectionId = await SeedPendingCleanupCorrectionAsync(fixture, independentId);
        fixture.Clock.UtcNow = Ceiling.AddTicks(-1);
        await using var writer = fixture.Context();
        var repository = fixture.Repository(writer, await fixture.AuthenticateAsync(writer));
        var live = await repository.PruneAsync(null, 1);
        await Assert.That(live.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Applied);
        await Assert.That(live.Purged).IsEqualTo(0);
        await using (var inspect = fixture.Context())
        {
            inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            await Assert.That(await inspect.ModerationNoticeSources.CountAsync()).IsEqualTo(4);
            await Assert.That((await inspect.ModerationNoticeSources.SingleAsync(row => row.EffectId == correctionId))
                .RetainUntil).IsEqualTo(Ceiling);
            await Assert.That(await inspect.ModerationNoticeDisclosureApprovals.CountAsync()).IsEqualTo(2);
        }
        fixture.Clock.UtcNow = Ceiling;
        var expired = await repository.PruneAsync(null, 1);
        await Assert.That(expired.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Applied);
        await Assert.That(expired.Purged).IsEqualTo(1);
        await Assert.That(expired.LastScannedApprovalId).IsEqualTo(firstApproval);
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationNoticeSources.CountAsync()).IsEqualTo(2);
        await Assert.That(await reader.ModerationNoticeSources.AnyAsync(row => row.EffectId == independentId)).IsTrue();
        await Assert.That(await reader.ModerationNoticeSources.AnyAsync(row => row.EffectId == independentCorrectionId)).IsTrue();
        await Assert.That(await reader.ModerationNoticeSources.AnyAsync(row => row.EffectId == correctionId)).IsFalse();
        await Assert.That(await reader.ModerationNotices.CountAsync()).IsEqualTo(4);
        await Assert.That(await reader.ModerationNoticeDisclosureApprovals.CountAsync()).IsEqualTo(1);
        await Assert.That((await reader.ModerationEffectIntents.SingleAsync(row => row.Id == correctionId)).CompletedAt).IsNull();
    }

    [Test]
    [Arguments("snapshot-anchor")]
    [Arguments("binding-anchor")]
    [Arguments("notice-anchor")]
    [Arguments("original-audience")]
    [Arguments("ceiling")]
    [Arguments("row-ceiling")]
    [Arguments("original-effect-anchor")]
    [Arguments("foreign-writer")]
    [Arguments("foreign-instance-writer")]
    [Arguments("missing-occurrence")]
    [Arguments("unexpected-recipient")]
    [Arguments("disposition")]
    [Arguments("acknowledgement")]
    [Arguments("native-pointer")]
    [Arguments("native-orphan")]
    public async Task Unproven_or_delivered_pending_correction_rolls_back_original_cleanup(string defect)
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var originalId = await fixture.SeedAsync(acknowledged: defect == "acknowledgement", nativePointer: defect == "native-pointer",
            instanceScoped: defect == "foreign-instance-writer");
        var correctionId = await SeedPendingCleanupCorrectionAsync(fixture, originalId, defect);
        var occurrences = await ReadCleanupOccurrencesAsync(fixture);
        await using var writer = fixture.Context();
        var result = await fixture.Repository(writer, await fixture.AuthenticateAsync(writer),
            nativeNotificationOwner: true).PruneAsync(null, 10);
        await Assert.That(result.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Unavailable);
        await Assert.That(result.LastScannedApprovalId).IsNull();
        await Assert.That(writer.Database.CurrentTransaction).IsNull();
        await Assert.That(writer.ModerationTrustDomainId).IsNull();
        await Assert.That(writer.ChangeTracker.Entries().Any()).IsFalse();
        await AssertPendingCleanupIntactAsync(fixture, originalId, correctionId, occurrences);
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        if (defect is "native-pointer" or "native-orphan")
            await Assert.That(await reader.NotificationIntents.IgnoreQueryFilters(["SoftDelete"]).CountAsync()).IsEqualTo(1);
        if (defect == "acknowledgement")
            await Assert.That((await reader.ModerationNoticeNotificationReceipts.SingleAsync()).EffectId).IsEqualTo(correctionId);
    }

    [Test]
    [Arguments("clock")]
    [Arguments("principal")]
    [Arguments("key")]
    [Arguments("trust-domain")]
    [Arguments("correction-revision")]
    public async Task Final_authority_loss_restores_original_and_pending_correction_payloads(string failure)
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var originalId = await fixture.SeedAsync();
        var correctionId = await SeedPendingCleanupCorrectionAsync(fixture, originalId);
        MachinePrincipalAccessor? machine = null;
        var occurrences = await ReadCleanupOccurrencesAsync(fixture);
        var observer = new CleanupSaved(async context =>
        {
            if (failure == "clock") fixture.Clock.UtcNow = Ceiling.AddTicks(-1);
            else if (failure == "principal") machine!.Clear();
            else if (failure == "trust-domain") context.ModerationTrustDomainId = null;
            else if (failure == "correction-revision")
                await context.ModerationEffectIntents.Where(row => row.Id == correctionId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Revision, 2L));
            else
                await context.ExternalApiKeys.Where(row => row.KeyId == fixture.KeyId).ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.ExternalApiKeyStatusId, (int)ExternalApiKeyStatusEnum.Revoked));
        });
        await using var writer = fixture.Context(observer);
        machine = await fixture.AuthenticateAsync(writer);
        var result = await fixture.Repository(writer, machine).PruneAsync(null, 10);
        await Assert.That(result.Outcome == ModerationNoticeCleanupOutcome.Applied).IsFalse();
        await Assert.That(observer.Observed).IsTrue();
        await Assert.That(writer.Database.CurrentTransaction).IsNull();
        await Assert.That(writer.ModerationTrustDomainId).IsNull();
        await AssertPendingCleanupIntactAsync(fixture, originalId, correctionId, occurrences);
    }

    [Test]
    public async Task Lost_correction_cleanup_commit_acknowledgement_cannot_recreate_or_complete_either_effect()
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var originalId = await fixture.SeedAsync();
        var correctionId = await SeedPendingCleanupCorrectionAsync(fixture, originalId);
        var fault = new LostCorrectionCleanupAcknowledgement();
        await using (var writer = fixture.Context(fault))
        {
            var result = await fixture.Repository(writer, await fixture.AuthenticateAsync(writer)).PruneAsync(null, 10);
            await Assert.That(result.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Unavailable);
            await Assert.That(result.LastScannedApprovalId).IsNull();
            await Assert.That(writer.Database.CurrentTransaction).IsNull();
        }
        await Assert.That(fault.Observed).IsTrue();
        await using var retry = fixture.Context();
        var replay = await fixture.Repository(retry, await fixture.AuthenticateAsync(retry)).PruneAsync(null, 10);
        await Assert.That(replay.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Applied);
        await Assert.That(replay.Purged).IsEqualTo(0);
        retry.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await retry.ModerationNoticeSources.CountAsync()).IsEqualTo(0);
        await Assert.That(await retry.ModerationNotices.CountAsync()).IsEqualTo(0);
        await Assert.That(await retry.ModerationEffectIntents.CountAsync()).IsEqualTo(2);
        await Assert.That(await retry.ModerationEffectIntents.AnyAsync(row => row.CompletedAt != null)).IsFalse();
        await Assert.That((await retry.ModerationEffectIntents.SingleAsync(row => row.Id == correctionId))
            .OriginalNoticeCleanupId).IsNull();
    }

    // Real retained Domain records exercise cleanup, not the independently owned capture writer.
    private static async Task<Guid> SeedPendingCleanupCorrectionAsync(
        CleanupDatabase fixture, Guid originalId, string? defect = null, bool legacyNullableProjection = false)
    {
        await using var context = fixture.Context();
        context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var originalEffect = await context.ModerationEffectIntents.SingleAsync(row => row.Id == originalId);
        var originalSource = await context.ModerationNoticeSources.SingleAsync(row => row.EffectId == originalId);
        var original = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(originalSource.SnapshotJson)!;
        var originals = await context.ModerationNotices.Where(row => row.EffectId == originalId)
            .OrderBy(row => row.Id).ToArrayAsync();
        var sanction = await context.ModerationSanctions.SingleAsync(row => row.Id == originalEffect.SanctionId);
        var at = OriginalAt.AddMinutes(2);
        sanction.Revoke(sanction.Revision, at);
        var effect = ModerationEffectIntent.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            originalEffect.DecisionId, originalEffect.DecisionRevision, sanction.Id, sanction.Revision,
            originalEffect.TrustDomainId, originalEffect.ScopeKind, originalEffect.ScopeId,
            originalEffect.SubjectKind, originalEffect.SubjectId, (int)ModerationOperationKind.Revoke,
            originalEffect.PolicyRevision, originalEffect.QualificationRevision, at, null, null);
        var pending = new ModerationNoticeCorrectionObligations(originalId,
            defect == "snapshot-anchor" ? new string('e', 64) : originalSource.SnapshotHash,
            defect == "binding-anchor" ? new string('f', 64) : originalSource.EffectBindingHash,
            Ceiling,
            originals.Select(row => new ModerationNoticeCorrectionRecipient(
                defect == "notice-anchor" && row.Audience == ModerationNoticeAudience.Reviewer ? Guid.CreateVersion7() : row.Id,
                row.Audience, row.RecipientSubjectKind,
                defect == "original-audience" && row.Audience == ModerationNoticeAudience.Reviewer
                    ? fixture.Database.AdministratorId : row.RecipientSubjectId,
                ModerationNoticeDeliveryDisposition.RecipientUnavailable))
                .ToImmutableArray());
        var capture = new ModerationNoticeCaptureSnapshot(2, "event.moderation.capture.v2",
            original.HostBindingId, original.HostSolutionId, original.HostInstanceId,
            defect is "foreign-writer" or "foreign-instance-writer" ? Guid.CreateVersion7() : original.WriterTenantId,
            original.AuthoritySourceId, effect.QualificationRevision, original.Decision, null, null, [],
            pending.RetainUntil)
        { PendingCorrection = pending };
        var source = ModerationNoticeSource.Capture(effect, legacyNullableProjection ? null : fixture.Database.TenantId,
            JsonSerializer.Serialize(capture), Ceiling);
        var occurrences = pending.Recipients.Select(row => ModerationNotice.CreatePendingCorrection(
            Guid.CreateVersion7(), source, row.Audience, row.RecipientSubjectKind, row.RecipientSubjectId)).ToArray();
        context.AddRange(effect, source, ModerationOperationReceipt.Complete(effect.OperationId, new string('b', 64),
            effect.TrustDomainId, effect.ScopeKind, effect.ScopeId, effect.SubjectKind, effect.SubjectId,
            fixture.Database.AdministratorId, new string('c', 64), effect.DecisionId, effect.SanctionId,
            sanction.Revision, effect.PolicyRevision, effect.QualificationRevision, at));
        context.AddRange(occurrences.Where(row => defect != "missing-occurrence" || row.Audience != ModerationNoticeAudience.Reviewer));
        if (defect == "unexpected-recipient")
            context.Add(ModerationNotice.Create(Guid.CreateVersion7(), source,
                ModerationNoticeAudience.Reviewer, 1, fixture.Database.AdministratorId, null,
                ModerationNoticeDeliveryDisposition.RecipientUnavailable));
        if (defect == "native-orphan")
            context.NotificationIntents.Add(new NotificationIntent
            {
                Id = Guid.CreateVersion7(),
                TenantId = fixture.Database.TenantId,
                CategoryId = (int)NotificationCategoryEnum.TrustSafetyModeration,
                OwnershipTypeId = (int)NotificationOwnershipTypeEnum.IslamuEvent,
                RecipientKindId = (int)NotificationRecipientKindEnum.User,
                StatusId = (int)NotificationIntentStatusEnum.Pending,
                RecipientUserId = fixture.Database.SubjectId,
                TemplateKey = "moderation.notice.portal.v1",
                DeduplicationKey = $"moderation-notice:{effect.Id:N}:1:{fixture.Database.SubjectId:N}",
                SafePayloadReference = $"moderation-notice:{occurrences.Single(row => row.Audience == ModerationNoticeAudience.Subject).Id:D}",
                SafePayloadHash = source.SnapshotHash,
                CreatedAt = at.UtcDateTime,
                IsDeleted = true
            });
        await context.SaveChangesAsync();
        if (defect == "ceiling")
        {
            var extended = JsonSerializer.Serialize(capture with
            {
                ValidUntil = Ceiling.AddTicks(1),
                PendingCorrection = pending with { RetainUntil = Ceiling.AddTicks(1) }
            });
            await context.ModerationNoticeSources.Where(row => row.EffectId == effect.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.SnapshotJson, extended));
        }
        if (defect == "row-ceiling")
            await context.ModerationNoticeSources.Where(row => row.EffectId == effect.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.RetainUntil, Ceiling.AddTicks(1)));
        if (defect == "original-effect-anchor")
        {
            var changed = capture with { PendingCorrection = pending with { OriginalEffectId = Guid.CreateVersion7() } };
            var changedSource = ModerationNoticeSource.Capture(effect, source.TenantId,
                JsonSerializer.Serialize(changed), Ceiling);
            await context.ModerationNoticeSources.Where(row => row.EffectId == effect.Id).ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.SnapshotJson, changedSource.SnapshotJson)
                    .SetProperty(row => row.SnapshotHash, changedSource.SnapshotHash));
        }
        if (defect == "native-pointer")
            await context.ModerationNotices.Where(row => row.EffectId == effect.Id
                    && row.Audience == ModerationNoticeAudience.Subject)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.NativeIntentId,
                        originals.Single(row => row.Audience == ModerationNoticeAudience.Subject).NativeIntentId)
                    .SetProperty(row => row.DeliveryDisposition, ModerationNoticeDeliveryDisposition.NativePortalEmailUnavailable));
        if (defect == "disposition")
            await context.ModerationNotices.Where(row => row.EffectId == effect.Id).ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.DeliveryDisposition, ModerationNoticeDeliveryDisposition.NativeRoutingUnavailable));
        if (defect == "acknowledgement")
            await context.ModerationNoticeNotificationReceipts.Where(row => row.EffectId == originalId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.EffectId, effect.Id)
                    .SetProperty(row => row.NoticeId, occurrences.Single(row => row.Audience == ModerationNoticeAudience.Subject).Id));
        return effect.Id;
    }

    private static async Task<(Guid EffectId, Guid SubjectNoticeId, string SnapshotHash)> SeedApprovedCleanupRevokeAsync(
        CleanupDatabase fixture, Guid originalId)
    {
        await using var context = fixture.Context();
        context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var originalEffect = await context.ModerationEffectIntents.SingleAsync(row => row.Id == originalId);
        var originalSource = await context.ModerationNoticeSources.SingleAsync(row => row.EffectId == originalId);
        var original = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(originalSource.SnapshotJson)!;
        var originalApproval = await context.ModerationNoticeDisclosureApprovals.SingleAsync(row => row.EffectId == originalId);
        var sanction = await context.ModerationSanctions.SingleAsync(row => row.Id == originalEffect.SanctionId);
        var at = OriginalAt.AddMinutes(2);
        sanction.Revoke(sanction.Revision, at);
        var effect = ModerationEffectIntent.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            originalEffect.DecisionId, originalEffect.DecisionRevision, sanction.Id, sanction.Revision,
            originalEffect.TrustDomainId, originalEffect.ScopeKind, originalEffect.ScopeId,
            originalEffect.SubjectKind, originalEffect.SubjectId, (int)ModerationOperationKind.Revoke,
            originalEffect.PolicyRevision, originalEffect.QualificationRevision, at, null, null);
        var text = new ModerationNoticeSafeText("Independently approved safe revocation explanation.");
        var approval = ModerationNoticeDisclosureApproval.Approve(originalApproval.Intent with
        {
            ApprovalId = Guid.CreateVersion7(),
            EffectId = effect.Id,
            RuleMeaning = text,
            ReasonMeaning = text,
            Facts = [text],
            RemainingActions = text,
            Recourse = text,
            Automation = text,
            Retention = text,
            ValidUntil = at.AddMinutes(1),
            RetainUntil = Ceiling
        }, fixture.Database.HostId, effect.TrustDomainId, fixture.Database.TenantId,
            fixture.Database.AdministratorId, 1, at);
        var capture = original with
        {
            Notice = original.Notice! with
            {
                EffectId = effect.Id,
                OperationId = effect.OperationId,
                OperationKind = effect.OperationKind,
                SanctionRevision = sanction.Revision,
                SanctionState = sanction.State,
                ApprovalId = approval.Id,
                RuleMeaning = text,
                ReasonMeaning = text,
                Facts = [text],
                RemainingActions = text,
                Recourse = text,
                Automation = text,
                Retention = text,
                CapturedAt = at,
                RetainUntil = Ceiling
            },
            ValidUntil = approval.ValidUntil
        };
        var source = ModerationNoticeSource.Capture(effect, originalSource.TenantId, JsonSerializer.Serialize(capture), Ceiling);
        await Assert.That(source.Matches(effect) && capture.Matches(effect)).IsTrue();
        context.AddRange(effect, source, approval, ModerationOperationReceipt.Complete(effect.OperationId, new string('b', 64),
            effect.TrustDomainId, effect.ScopeKind, effect.ScopeId, effect.SubjectKind, effect.SubjectId,
            fixture.Database.AdministratorId, new string('c', 64), effect.DecisionId, effect.SanctionId,
            sanction.Revision, effect.PolicyRevision, effect.QualificationRevision, at));
        var subjectNotice = ModerationNotice.Create(Guid.CreateVersion7(), source, ModerationNoticeAudience.Subject,
            effect.SubjectKind, effect.SubjectId, null, ModerationNoticeDeliveryDisposition.NativeRoutingUnavailable);
        context.Add(subjectNotice);
        context.Add(ModerationNotice.Create(Guid.CreateVersion7(), source, ModerationNoticeAudience.Reviewer,
            (int)ModerationSubjectKind.Human, fixture.Database.ReviewerId, null, ModerationNoticeDeliveryDisposition.NativeRoutingUnavailable));
        await context.SaveChangesAsync();
        return (effect.Id, subjectNotice.Id, source.SnapshotHash);
    }

    private static async Task<ModerationNotice[]> ReadCleanupOccurrencesAsync(CleanupDatabase fixture)
    {
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        return await reader.ModerationNotices.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync();
    }

    private static async Task AssertPendingCleanupIntactAsync(
        CleanupDatabase fixture, Guid originalId, Guid correctionId, ModerationNotice[] expectedOccurrences)
    {
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationNoticeSources.CountAsync()).IsEqualTo(2);
        await Assert.That(await reader.ModerationNoticeDisclosureApprovals.CountAsync()).IsEqualTo(1);
        await Assert.That(await reader.ModerationNoticeSources.AnyAsync(row => row.EffectId == correctionId)).IsTrue();
        var occurrences = await reader.ModerationNotices.OrderBy(row => row.Id).ToArrayAsync();
        foreach (var effectId in new[] { originalId, correctionId })
            await Assert.That(occurrences.Where(row => row.EffectId == effectId).Select(row => row.Id)
                .SequenceEqual(expectedOccurrences.Where(row => row.EffectId == effectId).Select(row => row.Id))).IsTrue();
        await Assert.That((await reader.ModerationEffectIntents.SingleAsync(row => row.Id == originalId))
            .OriginalNoticeCleanupId).IsNull();
        var correction = await reader.ModerationEffectIntents.SingleAsync(row => row.Id == correctionId);
        await Assert.That(correction.CompletedAt).IsNull();
        await Assert.That(correction.OriginalNoticeCleanupId).IsNull();
        await Assert.That(correction.Revision).IsEqualTo(1L);
        await Assert.That((await reader.ModerationSanctions.SingleAsync()).State).IsEqualTo(ModerationSanctionState.Revoked);
    }

    private sealed class LostCorrectionCleanupAcknowledgement : DbTransactionInterceptor
    {
        public bool Observed { get; private set; }
        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Observed = true;
            throw new TimeoutException("Pending correction cleanup commit acknowledgement unavailable.");
        }
    }
}
