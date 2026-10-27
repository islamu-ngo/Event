using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

// Uses the accepted original-execution boundary fixture, not an activation or conformance grant.
public sealed partial class ModerationResourceQuarantineExecutionTests
{
    [Test]
    public async Task Completion_does_not_treat_a_missing_original_cause_as_an_empty_recipe()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await CompleteOriginalAsync(fixture);
        await AssertCompletionPendingAsync(fixture, result,
            ModerationOriginalEffectCompletionPendingReason.OriginalCauseAccounting, 0);
    }

    [Test]
    public async Task Applied_original_cause_without_committed_review_remains_pending()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.That((await fixture.ExecuteAsync()).Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Applied);
        var result = await CompleteOriginalAsync(fixture);
        await AssertCompletionPendingAsync(fixture, result,
            ModerationOriginalEffectCompletionPendingReason.IndependentObjectReview, 1);
    }

    [Test]
    public async Task Durable_portal_occurrences_are_not_actual_notice_acknowledgement()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cause = (await fixture.ExecuteAsync()).Receipt!;
        await SeedCompletedReviewAsync(fixture, cause, safe: false);
        await SeedOriginalNoticeFactsAsync(fixture, acknowledge: false);
        var result = await CompleteOriginalAsync(fixture);
        await AssertCompletionPendingAsync(fixture, result,
            ModerationOriginalEffectCompletionPendingReason.OriginalNotification, 1);
    }

    [Test]
    public async Task Actual_acknowledgement_and_unsafe_review_do_not_fake_terminal_consumer_proof()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cause = (await fixture.ExecuteAsync()).Receipt!;
        await SeedCompletedReviewAsync(fixture, cause, safe: false);
        await SeedOriginalNoticeFactsAsync(fixture, acknowledge: true);
        var result = await CompleteOriginalAsync(fixture);
        await AssertCompletionPendingAsync(fixture, result,
            ModerationOriginalEffectCompletionPendingReason.TerminalConsumerProof, 1);
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await reader.Set<ModerationContentResponse>().CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.EventResourceModerationQuarantines.SingleAsync()).ReleaseId).IsNull();
    }

    [Test]
    public async Task Retired_original_cause_is_accounted_without_reactivation_or_automatic_completion()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cause = (await fixture.ExecuteAsync()).Receipt!;
        var review = await SeedCompletedReviewAsync(fixture, cause, safe: true);
        await SeedOriginalNoticeFactsAsync(fixture, acknowledge: true);
        var release = new EventResourceModerationQuarantineRelease
        {
            Id = Guid.CreateVersion7(),
            TenantId = cause.TenantId,
            QuarantineId = cause.Id,
            EventId = cause.EventId,
            EventResourceId = cause.EventResourceId,
            MaterialVersionId = cause.MaterialVersionId,
            OriginalEffectId = cause.EffectId,
            OriginalBindingOrdinal = cause.BindingOrdinal,
            OriginallyAppliedAt = cause.AppliedAt,
            ApprovalId = review.ApprovalId,
            ReleasedByHumanId = fixture.Database.AdministratorId,
            ReleasedAt = Now,
            RequestDigest = new string('A', 64)
        };
        await using (var writer = fixture.Context())
        {
            var resource = await writer.EventResources.Include(row => row.ModerationQuarantines)
                .SingleAsync(row => row.Id == fixture.ResourceId);
            resource.ReleaseModerationQuarantine(release, resource.ConcurrencyStamp);
            writer.Set<EventResourceModerationQuarantineRelease>().Add(release);
            await writer.SaveChangesAsync();
        }
        var result = await CompleteOriginalAsync(fixture);
        await AssertCompletionPendingAsync(fixture, result,
            ModerationOriginalEffectCompletionPendingReason.TerminalConsumerProof, 0);
        await using var reader = fixture.Context();
        await Assert.That((await reader.EventResourceModerationQuarantines.SingleAsync()).ReleaseId)
            .IsEqualTo(release.Id);
    }

    [Test]
    [Arguments("representative")]
    [Arguments("reviewer")]
    [Arguments("approver")]
    public async Task Retained_erasure_closes_completion_before_original_private_capture(string participant)
    {
        await using var fixture = await Fixture.CreateAsync();
        await EraseOriginalParticipantAsync(fixture, participant);
        var reads = new CaptureReads();
        var result = await CompleteOriginalAsync(fixture, interceptors: [reads]);
        await Assert.That(reads.Count).IsEqualTo(0);
        await AssertCompletionPendingAsync(fixture, result,
            ModerationOriginalEffectCompletionPendingReason.CurrentAuthority, 0);
    }

    [Test]
    public async Task Expired_custody_does_not_reconstruct_an_original_from_todays_policy()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Clock.UtcNow = Now.AddYears(1);
        var reads = new CaptureReads();
        var result = await CompleteOriginalAsync(fixture, interceptors: [reads]);
        await Assert.That(reads.Count).IsEqualTo(0);
        await AssertCompletionPendingAsync(fixture, result,
            ModerationOriginalEffectCompletionPendingReason.CurrentAuthority, 0);
    }

    [Test]
    public async Task Another_lease_or_tenant_cannot_complete_the_original_effect()
    {
        await using var fixture = await Fixture.CreateAsync();
        var wrongLease = await CompleteOriginalAsync(fixture, leaseId: Guid.CreateVersion7());
        await AssertCompletionPendingAsync(fixture, wrongLease,
            ModerationOriginalEffectCompletionPendingReason.CurrentAuthority, 0);
        var foreign = await CompleteOriginalAsync(fixture, tenantId: fixture.OtherTenantId);
        await AssertCompletionPendingAsync(fixture, foreign,
            ModerationOriginalEffectCompletionPendingReason.CurrentAuthority, 0);
    }

    [Test]
    public async Task Authentication_snapshot_cannot_survive_native_key_expiry()
    {
        await using var fixture = await Fixture.CreateAsync();
        var reads = new CaptureReads();
        var result = await CompleteOriginalAsync(fixture, expireKeyAfterAuthentication: true, interceptors: [reads]);
        await Assert.That(reads.Count).IsEqualTo(0);
        await AssertCompletionPendingAsync(fixture, result,
            ModerationOriginalEffectCompletionPendingReason.CurrentAuthority, 0);
    }

    [Test]
    public async Task Revoked_original_representative_cannot_be_replaced_with_recoursal_prose()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cause = (await fixture.ExecuteAsync()).Receipt!;
        await SeedCompletedReviewAsync(fixture, cause, safe: false);
        await SeedOriginalNoticeFactsAsync(fixture, acknowledge: true);
        await using (var writer = fixture.Context())
            await writer.EventRoleAssignments.Where(row => row.UserId == fixture.Database.SubjectId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAtUtc, Now.UtcDateTime));
        var result = await CompleteOriginalAsync(fixture);
        await AssertCompletionPendingAsync(fixture, result,
            ModerationOriginalEffectCompletionPendingReason.ProtectedContentRecourse, 1);
    }

    private static async Task AssertCompletionPendingAsync(Fixture fixture,
        ModerationOriginalEffectCompletionResult result, ModerationOriginalEffectCompletionPendingReason reason,
        int activeCauses)
    {
        await Assert.That(result.Outcome).IsEqualTo(ModerationOriginalEffectCompletionOutcome.Pending);
        await Assert.That(result.PendingReason).IsEqualTo(reason);
        await Assert.That(result.Receipt).IsNull();
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var effect = await reader.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == fixture.EffectId);
        await Assert.That(effect.CompletedAt).IsNull();
        await Assert.That(effect.Revision).IsEqualTo(fixture.LeaseRevision);
        await Assert.That(effect.LeaseId).IsEqualTo(fixture.LeaseId);
        await Assert.That(await reader.ModerationOriginalEffectCompletionReceipts.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.EventResources.SingleAsync(row => row.Id == fixture.ResourceId))
            .ModerationQuarantineCount).IsEqualTo(activeCauses);
    }
}
