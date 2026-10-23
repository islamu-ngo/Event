using System.Data.Common;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    // Native intake/attribution, generation, selection and adoption are owning writes.
    // This fixture still seeds finding/decision/sanction/capture, so it does not
    // witness native activation or complete effects. Causes use the actual native writer.
    [Test]
    public async Task Tenant_owned_resource_finding_applies_only_its_exact_native_original_cause()
    {
        await using var fixture = await OriginalCaseScopeFixture.CreateAsync();
        var original = await fixture.ReadOriginalResourceLineageAsync();
        await Assert.That(fixture.Original.Case.Scope.Kind).IsEqualTo(ModerationScopeKind.Tenant);
        await Assert.That(fixture.Finding.Scope)
            .IsEqualTo(AuthorityScope.Resource(fixture.Original.Case.Scope, fixture.Database.ResourceId));

        var applied = await fixture.ExecuteNativeCauseAsync(original.Generation, "original");
        await Assert.That(applied.Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Applied);
        await Assert.That(applied.Receipt).IsNotNull();
        await Assert.That(applied.Receipt!.EventResourceId).IsEqualTo(original.Generation.ResourceId);
        await Assert.That(applied.Receipt.MaterialVersionId).IsEqualTo(original.Generation.MaterialVersionId);
        await Assert.That(applied.Receipt.BindingOrdinal).IsEqualTo(0);
        await Assert.That(applied.Receipt.EventId).IsEqualTo(original.Generation.EventId);

        var replay = await fixture.ExecuteNativeCauseAsync(original.Generation, "original");
        await Assert.That(replay.Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.AlreadyApplied);
        await Assert.That(replay.Receipt!.Id).IsEqualTo(applied.Receipt.Id);
        await Assert.That(replay.Receipt.AppliedAt).IsEqualTo(applied.Receipt.AppliedAt);
        await using (var reader = fixture.Context())
        {
            var resource = await reader.EventResources.AsNoTracking().Include(row => row.ModerationQuarantines)
                .SingleAsync(row => row.Id == original.Generation.ResourceId);
            await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(1);
            await Assert.That(resource.ModerationQuarantines.Single().Id).IsEqualTo(applied.Receipt.Id);
            await Assert.That(resource.MaterialVersionId).IsEqualTo(original.Generation.MaterialVersionId);
            await Assert.That((await reader.ModerationNoticeSources.AsNoTracking()
                .SingleAsync(row => row.EffectId == applied.Receipt.EffectId)).SnapshotJson)
                .IsEqualTo(original.CaptureJson);
        }

        var completion = await fixture.CompleteAsync();
        await Assert.That(completion.PendingReason)
            .IsEqualTo(ModerationOriginalEffectCompletionPendingReason.IndependentObjectReview);
        await fixture.AssertUncompletedAsync();

        var review = await fixture.PrepareNativeUnsafeReviewAsync(applied.Receipt.Id, original.Generation);
        await Assert.That((await fixture.CompleteAsync()).PendingReason)
            .IsEqualTo(ModerationOriginalEffectCompletionPendingReason.IndependentObjectReview);
        await using (var pending = fixture.Context())
            await Assert.That(await pending.Set<EventResourceModerationReviewCompletion>().AnyAsync()).IsFalse();

        var approved = await fixture.NativeReviewAsync(review.Approver, review.Approval);
        await Assert.That(approved.Outcome).IsEqualTo(ModerationResourceSafetyOutcome.Known);
        var approvedAt = approved.Receipt!.RecordedAt;
        var repeated = await fixture.NativeReviewAsync(review.Approver, review.Approval);
        await Assert.That(repeated.Outcome).IsEqualTo(ModerationResourceSafetyOutcome.Known);
        await Assert.That(repeated.Receipt!.Id).IsEqualTo(approved.Receipt.Id);
        await Assert.That(repeated.Receipt.RecordedAt).IsEqualTo(approvedAt);
        await Assert.That(repeated.Receipt.ValidUntil).IsEqualTo(review.Deadline);
        await using (var reviewed = fixture.Context())
        {
            var durable = await reviewed.Set<EventResourceModerationReviewCompletion>().SingleAsync();
            await Assert.That(durable.QuarantineId).IsEqualTo(applied.Receipt.Id);
            await Assert.That(durable.EventId).IsEqualTo(original.Generation.EventId);
            await Assert.That(durable.EventResourceId).IsEqualTo(original.Generation.ResourceId);
            await Assert.That(durable.MaterialVersionId).IsEqualTo(original.Generation.MaterialVersionId);
            await Assert.That(durable.PurposeId).IsEqualTo(review.PurposeId);
            await Assert.That(durable.ApprovalId).IsEqualTo(review.Approval.OperationId);
            await Assert.That(durable.AttestationId).IsEqualTo(review.Approval.PredecessorId!.Value);
            await Assert.That(durable.Disposition)
                .IsEqualTo(ResourceSafetyAttestation.IndependentlyInspectedUnsafeForCurrentAudience);
            await Assert.That(durable.CompletedAt).IsEqualTo(approvedAt);
            await Assert.That(await reviewed.Set<EventResourceModerationQuarantineReview>().CountAsync()).IsEqualTo(4);
            var resource = await reviewed.EventResources.AsNoTracking().Include(row => row.ModerationQuarantines)
                .SingleAsync(row => row.Id == original.Generation.ResourceId);
            await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(1);
            await Assert.That(resource.ModerationQuarantines.Single().Id).IsEqualTo(applied.Receipt.Id);
            await Assert.That(resource.ModerationQuarantines.Single().ReleaseId).IsNull();
            await Assert.That(resource.MaterialVersionId).IsEqualTo(original.Generation.MaterialVersionId);
            await Assert.That((await reviewed.ModerationNoticeSources.AsNoTracking()
                .SingleAsync(row => row.EffectId == applied.Receipt.EffectId)).SnapshotJson)
                .IsEqualTo(original.CaptureJson);
        }
        await Assert.That((await fixture.CompleteAsync()).PendingReason)
            .IsEqualTo(ModerationOriginalEffectCompletionPendingReason.OriginalNotification);
        await fixture.AssertUncompletedAsync();
    }

    [Test]
    public async Task Native_original_cause_review_and_final_approval_roll_back_at_owned_purpose_deadline()
    {
        await using var fixture = await OriginalCaseScopeFixture.CreateAsync();
        var original = await fixture.ReadOriginalResourceLineageAsync();
        var applied = await fixture.ExecuteNativeCauseAsync(original.Generation, "original");
        await Assert.That(applied.Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Applied);
        var review = await fixture.PrepareNativeUnsafeReviewAsync(applied.Receipt!.Id, original.Generation);
        var checkpoint = new OriginalReviewDeadlineCheckpoint(fixture.Database.Clock, review.Deadline);
        var expired = await fixture.NativeReviewAsync(review.Approver, review.Approval, checkpoint);
        await Assert.That(checkpoint.Observed).IsTrue();
        await Assert.That(expired.Outcome).IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        await using (var reader = fixture.Context())
        {
            await Assert.That(await reader.Set<EventResourceModerationReviewCompletion>().AnyAsync()).IsFalse();
            await Assert.That(await reader.Set<EventResourceModerationQuarantineReview>()
                .AnyAsync(row => row.Id == review.Approval.OperationId)).IsFalse();
            var resource = await reader.EventResources.AsNoTracking().Include(row => row.ModerationQuarantines)
                .SingleAsync(row => row.Id == original.Generation.ResourceId);
            await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(1);
            await Assert.That(resource.ModerationQuarantines.Single().ReleaseId).IsNull();
            await Assert.That(resource.MaterialVersionId).IsEqualTo(original.Generation.MaterialVersionId);
        }
        await Assert.That((await fixture.CompleteAsync()).PendingReason)
            .IsEqualTo(ModerationOriginalEffectCompletionPendingReason.IndependentObjectReview);
        await fixture.AssertUncompletedAsync();
    }

    private sealed class OriginalReviewDeadlineCheckpoint(NativeClock clock, DateTimeOffset deadline)
        : SaveChangesInterceptor
    {
        public bool Observed { get; private set; }

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!Observed && eventData.Context!.ChangeTracker.Entries<EventResourceModerationReviewCompletion>().Any())
            {
                Observed = true;
                clock.Now = deadline;
            }
            return ValueTask.FromResult(result);
        }
    }

    [Test]
    [Arguments("case")]
    [Arguments("resource")]
    [Arguments("identity")]
    public async Task Tenant_custody_does_not_authorize_a_substituted_native_original_finding(string change)
    {
        await using var fixture = await OriginalCaseScopeFixture.CreateAsync();
        var original = await fixture.ReadOriginalResourceLineageAsync();
        await Assert.That(await fixture.AcquireRecipeAsync()).IsTrue();
        await fixture.SubstituteFindingAsync(change);

        var result = await fixture.ExecuteNativeCauseAsync(original.Generation, "original");
        await Assert.That(result.Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Unavailable);
        await Assert.That(result.Receipt).IsNull();
        await fixture.AssertUncompletedAsync();
        await using var reader = fixture.Context();
        var resource = await reader.EventResources.AsNoTracking()
            .SingleAsync(row => row.Id == original.Generation.ResourceId);
        await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(0);
        await Assert.That(resource.MaterialVersionId).IsEqualTo(original.Generation.MaterialVersionId);
        await Assert.That(resource.ConcurrencyStamp).IsEqualTo(original.ResourceStamp);
    }

    [Test]
    public async Task Native_original_resource_lineage_without_application_remains_unaccounted()
    {
        await using var fixture = await OriginalCaseScopeFixture.CreateAsync();
        var lineage = await fixture.ReadOriginalResourceLineageAsync();
        await Assert.That(lineage.Notice.Effects.Length).IsEqualTo(1);
        await Assert.That(lineage.Adoption.BindingOrdinal).IsEqualTo(0);
        await Assert.That(lineage.Adoption.GenerationId).IsEqualTo(lineage.Generation.Id);
        await Assert.That(lineage.Adoption.GenerationRevision).IsEqualTo(lineage.Generation.Revision);
        await Assert.That(lineage.Adoption.GenerationWriterGeneration).IsEqualTo(lineage.Generation.WriterGeneration);
        await Assert.That(lineage.Generation.EventId).IsEqualTo(fixture.Database.EventId);
        await Assert.That(lineage.Generation.ResourceId).IsEqualTo(fixture.Database.ResourceId);
        await Assert.That(lineage.Notice.Effects[0].Target.Source!.Id).IsEqualTo(lineage.Generation.Id);
        await Assert.That(lineage.Adoption.CaseId).IsEqualTo(fixture.Original.Case.Id);
        await Assert.That(fixture.Original.Case.Scope.Kind).IsEqualTo(ModerationScopeKind.Tenant);
        await Assert.That(fixture.Finding.Scope.Kind).IsEqualTo(ModerationScopeKind.Resource);

        var checkpoint = new OriginalCauseAccountingCheckpoint();
        var result = await fixture.CompleteAsync(checkpoint);
        await Assert.That(checkpoint.AccountingReads).IsEqualTo(1);
        await Assert.That(result.PendingReason)
            .IsEqualTo(ModerationOriginalEffectCompletionPendingReason.OriginalCauseAccounting);
        await fixture.AssertUncompletedAsync();
        await fixture.AssertOriginalResourceUnchangedAsync(lineage);
    }

    [Test]
    public async Task A_later_native_generation_cannot_replace_the_original_generation_or_create_its_cause()
    {
        await using var fixture = await OriginalCaseScopeFixture.CreateAsync();
        var original = await fixture.ReadOriginalResourceLineageAsync();
        var replacementId = Guid.CreateVersion7();
        await fixture.AcceptAsync(fixture.Original.Assessor, new RecordModerationResourceGenerationSource
        {
            OperationId = Guid.CreateVersion7(), Target = fixture.Original.Case.Scope,
            ExpectedQualificationRevision = 1, SourceId = replacementId,
            ResourceId = fixture.Database.ResourceId, ExpectedResourceRevision = original.Generation.Revision,
            ValidUntil = original.Generation.ValidUntil
        });
        await using (var reader = fixture.Context())
        {
            var replacement = await reader.ModerationResourceGenerations.AsNoTracking()
                .SingleAsync(row => row.Id == replacementId);
            await Assert.That(replacement.Revision).IsEqualTo(2);
            await Assert.That(replacement.ResourceId).IsEqualTo(original.Generation.ResourceId);
            await Assert.That(replacement.MaterialVersionId).IsEqualTo(original.Generation.MaterialVersionId);
            await Assert.That(replacement.WriterGeneration > original.Generation.WriterGeneration).IsTrue();
        }
        var execution = await fixture.ExecuteNativeOriginalAsync();
        await Assert.That(execution.Receipts.Count).IsEqualTo(0);
        await Assert.That(execution.Outcome).IsEqualTo(ModerationOriginalQuarantineOutcome.Partial);
        await Assert.That((await fixture.CompleteAsync()).Outcome)
            .IsEqualTo(ModerationOriginalEffectCompletionOutcome.Pending);
        await fixture.AssertUncompletedAsync();
        await fixture.AssertOriginalResourceUnchangedAsync(original);
    }

    [Test]
    public async Task Native_adoption_revocation_cannot_be_renewed_by_original_completion()
    {
        await using var fixture = await OriginalCaseScopeFixture.CreateAsync();
        var original = await fixture.ReadOriginalResourceLineageAsync();
        await fixture.AcceptAsync(fixture.Original.Assessor, new RevokeModerationResourceTargetSource
        {
            OperationId = Guid.CreateVersion7(), Target = fixture.Finding.Scope,
            ExpectedQualificationRevision = 1, AdoptionId = original.Adoption.Id,
            ExpectedAdoptionRevision = original.Adoption.Revision,
            CaseId = fixture.Original.Case.Id, ExpectedCaseRevision = fixture.Original.Case.Revision
        });
        var result = await fixture.CompleteAsync();
        await Assert.That(result.Outcome).IsEqualTo(ModerationOriginalEffectCompletionOutcome.Pending);
        await fixture.AssertUncompletedAsync();
        await using var reader = fixture.Context();
        var adoption = await reader.ModerationResourceTargetAdoptions.AsNoTracking()
            .SingleAsync(row => row.Id == original.Adoption.Id);
        await Assert.That(adoption.Revision).IsEqualTo(2);
        await Assert.That(adoption.RevokedAt).IsEqualTo(fixture.Database.Clock.Now);
        await Assert.That(adoption.GenerationId).IsEqualTo(original.Generation.Id);
        await Assert.That(adoption.SelectionId).IsEqualTo(original.Adoption.SelectionId);
        await Assert.That(adoption.ValidUntil).IsEqualTo(original.Adoption.ValidUntil);
        await fixture.AssertOriginalResourceUnchangedAsync(original);
    }

    [Test]
    public async Task A_substituted_adoption_identity_cannot_revoke_or_replace_the_original_ordinal()
    {
        await using var fixture = await OriginalCaseScopeFixture.CreateAsync();
        var original = await fixture.ReadOriginalResourceLineageAsync();
        await fixture.AcceptAsync(fixture.Original.Assessor, new RevokeModerationResourceTargetSource
        {
            OperationId = Guid.CreateVersion7(), Target = fixture.Finding.Scope,
            ExpectedQualificationRevision = 1, AdoptionId = Guid.CreateVersion7(),
            ExpectedAdoptionRevision = original.Adoption.Revision,
            CaseId = fixture.Original.Case.Id, ExpectedCaseRevision = fixture.Original.Case.Revision
        }, ModerationQualificationWriteOutcome.NotFound);
        await using (var reader = fixture.Context())
        {
            var adoption = await reader.ModerationResourceTargetAdoptions.AsNoTracking()
                .SingleAsync(row => row.Id == original.Adoption.Id);
            await Assert.That(adoption.RevokedAt).IsNull();
            await Assert.That(adoption.Revision).IsEqualTo(1);
            await Assert.That(adoption.GenerationId).IsEqualTo(original.Generation.Id);
            await Assert.That(adoption.BindingOrdinal).IsEqualTo(0);
        }
        await Assert.That((await fixture.CompleteAsync()).PendingReason)
            .IsEqualTo(ModerationOriginalEffectCompletionPendingReason.OriginalCauseAccounting);
        await fixture.AssertUncompletedAsync();
        await fixture.AssertOriginalResourceUnchangedAsync(original);
    }

    [Test]
    [Arguments("effect")]
    [Arguments("lease")]
    [Arguments("ordinal")]
    [Arguments("resource")]
    [Arguments("material")]
    public async Task Substituted_native_cause_coordinates_cannot_manufacture_original_accounting(string coordinate)
    {
        await using var fixture = await OriginalCaseScopeFixture.CreateAsync();
        var original = await fixture.ReadOriginalResourceLineageAsync();
        var result = await fixture.ExecuteNativeCauseAsync(original.Generation, coordinate);
        await Assert.That(result.Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Unavailable);
        await Assert.That(result.Receipt).IsNull();
        var checkpoint = new OriginalCauseAccountingCheckpoint();
        var completion = await fixture.CompleteAsync(checkpoint);
        await Assert.That(checkpoint.AccountingReads).IsEqualTo(1);
        await Assert.That(completion.PendingReason)
            .IsEqualTo(ModerationOriginalEffectCompletionPendingReason.OriginalCauseAccounting);
        await fixture.AssertUncompletedAsync();
        await fixture.AssertOriginalResourceUnchangedAsync(original);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Independent_native_review_or_release_cannot_stand_in_for_an_absent_original_cause(bool release)
    {
        await using var fixture = await OriginalCaseScopeFixture.CreateAsync();
        var original = await fixture.ReadOriginalResourceLineageAsync();
        await using (var context = fixture.Context())
        {
            var reviewer = await context.ModerationReviewerAssignments.AsNoTracking()
                .SingleAsync(row => row.CaseId == fixture.Original.Case.Id);
            var (admin, machine) = fixture.Identity(context, reviewer.ReviewerHumanId);
            var owner = new ModerationResourceQuarantineReleaseRepository(context, admin, machine, fixture.Database.Clock);
            if (release)
            {
                var result = await owner.ReleaseAsync(new(Guid.CreateVersion7(), Guid.CreateVersion7(),
                    original.Generation.ResourceId, original.Generation.MaterialVersionId, Guid.CreateVersion7()));
                await Assert.That(result.Outcome).IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
                await Assert.That(result.Receipt).IsNull();
            }
            else
            {
                var result = await owner.ReviewAsync(new()
                {
                    OperationId = Guid.CreateVersion7(), QuarantineId = Guid.CreateVersion7(),
                    ResourceId = original.Generation.ResourceId, MaterialVersionId = original.Generation.MaterialVersionId,
                    CaseId = fixture.Original.Case.Id, ExpectedCaseRevision = fixture.Original.Case.Revision,
                    ReviewerAssignmentId = reviewer.Id, ExpectedReviewerRevision = reviewer.Revision,
                    Step = ResourceSafetyReviewStep.ProposePurpose, RequiredApprovals = 1,
                    MaximumEvidenceLifetime = TimeSpan.FromSeconds(1), ValidUntil = original.Generation.ValidUntil
                });
                await Assert.That(result.Outcome).IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
                await Assert.That(result.Receipt).IsNull();
            }
        }
        var completion = await fixture.CompleteAsync();
        await Assert.That(completion.PendingReason)
            .IsEqualTo(ModerationOriginalEffectCompletionPendingReason.OriginalCauseAccounting);
        await fixture.AssertUncompletedAsync();
        await fixture.AssertOriginalResourceUnchangedAsync(original);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Original_deadline_expiry_at_capture_checkpoint_cannot_renew_native_resource_authority(bool custody)
    {
        await using var fixture = await OriginalCaseScopeFixture.CreateAsync();
        var original = await fixture.ReadOriginalResourceLineageAsync();
        var deadline = custody ? original.Notice.RetainUntil : original.Generation.ValidUntil;
        var checkpoint = new OriginalCauseAccountingCheckpoint(() => fixture.Database.Clock.Now = deadline);
        var result = await fixture.CompleteAsync(checkpoint);
        await Assert.That(checkpoint.ExpiredAtCapture).IsTrue();
        // Custody is rechecked immediately after capture. With no native cause,
        // accounting must remain pending even if its entry-time observation predates expiry.
        await Assert.That(checkpoint.AccountingReads).IsEqualTo(custody ? 0 : 1);
        await Assert.That(result.Outcome).IsEqualTo(ModerationOriginalEffectCompletionOutcome.Pending);
        await Assert.That(result.Receipt).IsNull();
        await fixture.AssertUncompletedAsync();
        await fixture.AssertOriginalResourceUnchangedAsync(original);
    }

    private sealed class OriginalCauseAccountingCheckpoint(Action? expire = null) : DbCommandInterceptor
    {
        public int AccountingReads { get; private set; }
        public bool ExpiredAtCapture { get; private set; }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("event_resource_moderation_quarantines", StringComparison.Ordinal)
                && command.CommandText.Contains("ORDER BY", StringComparison.Ordinal)
                && command.CommandText.Contains("binding_ordinal", StringComparison.Ordinal))
                AccountingReads++;
            if (expire is not null && !ExpiredAtCapture
                && Enumerable.Range(0, result.FieldCount).Any(index => result.GetName(index) == "snapshot_json"))
            {
                ExpiredAtCapture = true;
                expire();
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed record OriginalResourceLineage(
        ModerationResourceGeneration Generation, ModerationResourceTargetAdoption Adoption,
        ModerationNoticeSnapshot Notice, string CaptureJson, Guid ResourceStamp);

    private sealed partial class OriginalCaseScopeFixture
    {
        public async Task<OriginalResourceLineage> ReadOriginalResourceLineageAsync()
        {
            await using var reader = Context();
            var source = await reader.ModerationNoticeSources.AsNoTracking().SingleAsync(row => row.EffectId == EffectId);
            var capture = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson)!;
            var generation = await reader.ModerationResourceGenerations.AsNoTracking().SingleAsync();
            var adoption = await reader.ModerationResourceTargetAdoptions.AsNoTracking()
                .SingleAsync(row => row.BindingOrdinal == 0);
            var resource = await reader.EventResources.AsNoTracking().SingleAsync(row => row.Id == Database.ResourceId);
            return new(generation, adoption, capture.Notice!, source.SnapshotJson, resource.ConcurrencyStamp);
        }

        public async Task<ModerationOriginalQuarantineResult> ExecuteNativeOriginalAsync()
        {
            await using var context = Context();
            var (admin, machine) = Identity(context);
            return await new ModerationResourceQuarantineRepository(context, machine, Bindings(context, admin, machine),
                new ExternalApiKeyRepository(context), Database.Clock, Original.Privacy.Authority(context, Database.Clock))
                .ExecuteOriginalAsync(EffectId, LeaseId, LeaseRevision);
        }

        public async Task<ModerationResourceQuarantineResult> ExecuteNativeCauseAsync(
            ModerationResourceGeneration generation, string coordinate)
        {
            await using var context = Context();
            var (admin, machine) = Identity(context);
            return await new ModerationResourceQuarantineRepository(context, machine, Bindings(context, admin, machine),
                new ExternalApiKeyRepository(context), Database.Clock, Original.Privacy.Authority(context, Database.Clock))
                .ExecuteAsync(coordinate == "effect" ? Guid.CreateVersion7() : EffectId,
                    coordinate == "lease" ? Guid.CreateVersion7() : LeaseId, LeaseRevision,
                    coordinate == "ordinal" ? 1 : 0,
                    coordinate == "resource" ? Guid.CreateVersion7() : generation.ResourceId,
                    coordinate == "material" ? Guid.CreateVersion7() : generation.MaterialVersionId);
        }

        public async Task AssertOriginalResourceUnchangedAsync(OriginalResourceLineage originalLineage)
        {
            await using var reader = Context();
            var resource = await reader.EventResources.AsNoTracking().SingleAsync(row => row.Id == Database.ResourceId);
            await Assert.That(resource.ConcurrencyStamp).IsEqualTo(originalLineage.ResourceStamp);
            await Assert.That(resource.MaterialVersionId).IsEqualTo(originalLineage.Generation.MaterialVersionId);
            await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(0);
            await Assert.That((await reader.ModerationNoticeSources.AsNoTracking()
                .SingleAsync(row => row.EffectId == EffectId)).SnapshotJson).IsEqualTo(originalLineage.CaptureJson);
            var generation = await reader.ModerationResourceGenerations.AsNoTracking()
                .SingleAsync(row => row.Id == originalLineage.Generation.Id);
            await Assert.That(generation.Revision).IsEqualTo(originalLineage.Generation.Revision);
            await Assert.That(generation.WriterGeneration).IsEqualTo(originalLineage.Generation.WriterGeneration);
            await Assert.That(generation.ValidUntil).IsEqualTo(originalLineage.Generation.ValidUntil);
            var adoption = await reader.ModerationResourceTargetAdoptions.AsNoTracking()
                .SingleAsync(row => row.Id == originalLineage.Adoption.Id);
            await Assert.That(adoption.GenerationId).IsEqualTo(originalLineage.Generation.Id);
            await Assert.That(adoption.BindingOrdinal).IsEqualTo(originalLineage.Adoption.BindingOrdinal);
            await Assert.That(adoption.ValidUntil).IsEqualTo(originalLineage.Adoption.ValidUntil);
            await Assert.That(await reader.Set<EventResourceModerationQuarantineReview>().AnyAsync()).IsFalse();
            await Assert.That(await reader.Set<EventResourceModerationReviewCompletion>().AnyAsync()).IsFalse();
            await Assert.That(await reader.Set<EventResourceModerationQuarantineRelease>().AnyAsync()).IsFalse();
        }
    }
}
