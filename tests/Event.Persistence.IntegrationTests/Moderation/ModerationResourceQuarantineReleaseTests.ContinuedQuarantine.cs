using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceQuarantineReleaseTests
{
    [Test]
    public async Task UnsafeInspectionAndIndependentApprovalCompleteReviewWithoutReleasingEitherCause()
    {
        await using var fixture = await Fixture.CreateAsync(prepare: false);
        var purpose = fixture.Intent(fixture.Author, ResourceSafetyReviewStep.ProposePurpose, null) with
        {
            RequiredApprovals = 1, MaximumEvidenceLifetime = TimeSpan.FromMinutes(10)
        };
        await fixture.AcceptAsync(fixture.Author, purpose);
        var adoption = fixture.Intent(fixture.Approver, ResourceSafetyReviewStep.AdoptPurpose, purpose.OperationId);
        await fixture.AcceptAsync(fixture.Approver, adoption);
        var inspection = fixture.Intent(fixture.Author, ResourceSafetyReviewStep.AttestMaterialSafety, adoption.OperationId) with
        {
            Attestation = ResourceSafetyAttestation.IndependentlyInspectedUnsafeForCurrentAudience
        };
        await fixture.AcceptAsync(fixture.Author, inspection);
        await using (var reader = fixture.Context())
            await Assert.That(await reader.Set<EventResourceModerationReviewCompletion>().CountAsync()).IsEqualTo(0);
        var safeApproval = fixture.Intent(fixture.Approver, ResourceSafetyReviewStep.ApproveSafety, inspection.OperationId);
        await Assert.That((await fixture.ReviewAsync(fixture.Approver, safeApproval)).Outcome)
            .IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        var approval = fixture.Intent(fixture.Approver, ResourceSafetyReviewStep.ApproveContinuedQuarantine, inspection.OperationId);
        await fixture.AcceptAsync(fixture.Approver, approval);
        var release = new ModerationResourceQuarantineReleaseIntent(Guid.CreateVersion7(), fixture.CauseId,
            fixture.ResourceId, fixture.MaterialId, approval.OperationId);
        await Assert.That((await fixture.ReleaseAsync(release)).Outcome).IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        await RequireUnreleasedAsync(fixture);
        fixture.Clock.Now = fixture.Deadline.AddDays(1);
        await using var completed = fixture.Context();
        var receipt = await completed.Set<EventResourceModerationReviewCompletion>().SingleAsync();
        await Assert.That(receipt.Disposition).IsEqualTo(ResourceSafetyAttestation.IndependentlyInspectedUnsafeForCurrentAudience);
        await Assert.That(receipt.ApprovalId).IsEqualTo(approval.OperationId);
        await Assert.That(receipt.MaterialVersionId).IsEqualTo(fixture.MaterialId);
        await Assert.That(receipt.CompletedAt).IsLessThan(fixture.Deadline);
    }

    [Test]
    public async Task IncompleteQuorumDoesNotDischargeReviewDuty()
    {
        await using var fixture = await Fixture.CreateAsync(approvals: 2);
        await using var reader = fixture.Context();
        await Assert.That(await reader.Set<EventResourceModerationReviewCompletion>().CountAsync()).IsEqualTo(0);
        await RequireUnreleasedAsync(fixture);
    }

    [Test]
    public async Task CompletionAndFinalApprovalRollBackTogetherAtPurposeDeadline()
    {
        await using var fixture = await Fixture.CreateAsync(approvals: 2);
        var last = fixture.Intent(fixture.Database.AdministratorId, ResourceSafetyReviewStep.ApproveSafety, fixture.ApprovalId);
        var expired = new ExpireAfterFlush(fixture.Clock, fixture.Deadline);
        await Assert.That((await fixture.ReviewAsync(fixture.Database.AdministratorId, last, expired)).Outcome)
            .IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        await using var reader = fixture.Context();
        await Assert.That(await reader.Set<EventResourceModerationReviewCompletion>().CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.Set<EventResourceModerationQuarantineReview>().AnyAsync(row => row.Id == last.OperationId))
            .IsFalse();
    }
}
