using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Domain;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceQuarantineReleaseTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FreshIndependentReviewReleasesOpaqueCauseWithoutReversingHistory(bool storedFile)
    {
        await using var fixture = await Fixture.CreateAsync(storedFile);
        var intent = fixture.ReleaseIntent();
        await using (var context = fixture.Context())
        {
            var identity = Identity(context, fixture.Approver);
            var response = await new ReleaseModerationResourceQuarantineCommandHandler(
                new ModerationResourceQuarantineReleaseRepository(context, identity.Admin, identity.Machine, fixture.Clock))
                .ExecuteAsync(new(intent));
            await Assert.That(response.IsSuccess).IsTrue();
            await Assert.That(response.Id).IsEqualTo(intent.OperationId);
            await Assert.That(response.RecordedAt).IsEqualTo(fixture.Clock.Now);
        }
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var resource = await reader.EventResources.Include(row => row.ModerationQuarantines)
            .SingleAsync(row => row.Id == fixture.ResourceId);
        var cause = await reader.EventResourceModerationQuarantines.SingleAsync(row => row.Id == fixture.CauseId);
        var receipt = await reader.Set<EventResourceModerationQuarantineRelease>().SingleAsync(row => row.Id == intent.OperationId);
        var history = await reader.ModerationFindings.SingleAsync(row => row.Id == fixture.HistoricalFindingId);
        await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(1);
        await Assert.That(resource.ModerationQuarantines.Single().Id).IsEqualTo(fixture.OtherCauseId);
        await Assert.That(resource.HasPublishablePayload()).IsFalse();
        await Assert.That(cause.ReleaseId).IsEqualTo(receipt.Id);
        await Assert.That(receipt.OriginallyAppliedAt).IsEqualTo(OriginalTime);
        await Assert.That(receipt.Owns(cause)).IsTrue();
        await Assert.That(history.State).IsEqualTo(ModerationFindingState.Final);
        await Assert.That(history.Revision).IsEqualTo(1L);
        await Assert.That(history.ReversalReferenceId).IsNull();
        await Assert.That(await reader.ModerationNoticeSources.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task ExactReplayAfterFreshPurposeExpiryReturnsOriginalReceiptAndTime()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intent = fixture.ReleaseIntent();
        var original = await fixture.ReleaseAsync(intent);
        await Assert.That(original.Outcome).IsEqualTo(ModerationResourceSafetyOutcome.Known);
        fixture.Clock.Now = fixture.Deadline.AddDays(1);
        var replay = await fixture.ReleaseAsync(intent);
        await Assert.That(replay.Outcome).IsEqualTo(ModerationResourceSafetyOutcome.Known);
        await Assert.That(replay.Receipt!.Id).IsEqualTo(original.Receipt!.Id);
        await Assert.That(replay.Receipt.ReleasedAt).IsEqualTo(original.Receipt.ReleasedAt);
        var substitution = await fixture.ReleaseAsync(intent with { ApprovalId = Guid.CreateVersion7() });
        await Assert.That(substitution.Outcome).IsEqualTo(ModerationResourceSafetyOutcome.Conflict);
        await using var reader = fixture.Context();
        await Assert.That(await reader.Set<EventResourceModerationQuarantineRelease>().CountAsync()).IsEqualTo(1);
        await Assert.That((await reader.EventResources.SingleAsync(row => row.Id == fixture.ResourceId)).ModerationQuarantineCount)
            .IsEqualTo(1);
    }

    [Test]
    public async Task EveryIndependentCauseRequiresItsOwnFreshChain()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = fixture.ReleaseIntent();
        var borrowed = await fixture.ReleaseAsync(first with { QuarantineId = fixture.OtherCauseId });
        await Assert.That(borrowed.Outcome).IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        await Assert.That((await fixture.ReleaseAsync(first)).Outcome).IsEqualTo(ModerationResourceSafetyOutcome.Known);
        await fixture.PrepareAsync(fixture.OtherCauseId);
        await Assert.That((await fixture.ReleaseAsync(fixture.ReleaseIntent())).Outcome).IsEqualTo(ModerationResourceSafetyOutcome.Known);
        await using var reader = fixture.Context();
        var resource = await reader.EventResources.SingleAsync(row => row.Id == fixture.ResourceId);
        await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(0);
        await Assert.That(resource.HasPublishablePayload()).IsTrue();
        await Assert.That(await reader.Set<EventResourceModerationQuarantineRelease>().CountAsync()).IsEqualTo(2);
        await Assert.That(await reader.EventResourceModerationQuarantines.CountAsync()).IsEqualTo(2);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task CurrentMaterialNativeStampAndExactResourceCannotBeSubstituted(int substitution)
    {
        await using var fixture = await Fixture.CreateAsync();
        var intent = fixture.ReleaseIntent();
        if (substitution == 0) intent = intent with { MaterialVersionId = Guid.CreateVersion7() };
        if (substitution == 1) intent = intent with { ResourceId = Guid.CreateVersion7() };
        if (substitution == 2)
        {
            await using var writer = fixture.Context();
            var resource = await writer.EventResources.SingleAsync(row => row.Id == fixture.ResourceId);
            resource.ConcurrencyStamp = Guid.NewGuid();
            await writer.SaveChangesAsync();
        }
        await Assert.That((await fixture.ReleaseAsync(intent)).Outcome).IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        await RequireUnreleasedAsync(fixture);
    }

    [Test]
    public async Task DeletedMaterialCannotBeRestoredByPreviouslyApprovedReview()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var writer = fixture.Context())
        {
            var resource = await writer.EventResources.SingleAsync(row => row.Id == fixture.ResourceId);
            resource.Delete(resource.ConcurrencyStamp, fixture.Database.AdministratorId, fixture.Clock.Now.UtcDateTime);
            await writer.SaveChangesAsync();
        }
        await Assert.That((await fixture.ReleaseAsync(fixture.ReleaseIntent())).Outcome)
            .IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        await using var reader = fixture.Context();
        await Assert.That(await reader.EventResources.AnyAsync(row => row.Id == fixture.ResourceId)).IsFalse();
        await Assert.That((await reader.EventResourceModerationQuarantines.SingleAsync(row => row.Id == fixture.CauseId)).ReleaseId).IsNull();
        await Assert.That(await reader.Set<EventResourceModerationQuarantineRelease>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CurrentReviewerWithdrawalOrProceduralWithdrawalClosesRelease(bool procedural)
    {
        await using var fixture = await Fixture.CreateAsync();
        if (procedural)
            await fixture.SourceAsync(fixture.Database.AdministratorId, new RevokeModerationQualificationPolicyAdoption
            {
                OperationId = Guid.CreateVersion7(), Target = fixture.Scope, ExpectedQualificationRevision = 1,
                SourceId = fixture.PolicySourceId, ExpectedSourceRevision = 1, AdoptionId = fixture.PolicyAdoptionId,
                ExpectedAdoptionRevision = 1, CaseId = fixture.CaseId, ExpectedCaseRevision = 1
            });
        else
            await fixture.SourceAsync(fixture.Database.AdministratorId, new WithdrawModerationReviewerSource
            {
                OperationId = Guid.CreateVersion7(), Target = fixture.Scope, ExpectedQualificationRevision = 1,
                AssignmentId = fixture.Assignments[fixture.Approver], ExpectedAssignmentRevision = 3
            });
        await Assert.That((await fixture.ReleaseAsync(fixture.ReleaseIntent())).Outcome)
            .IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        await RequireUnreleasedAsync(fixture);
    }

    [Test]
    public async Task WithdrawnFreshPurposeCannotAuthorizeAnEarlierApproval()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AcceptAsync(fixture.Author,
            fixture.Intent(fixture.Author, ResourceSafetyReviewStep.WithdrawPurpose, fixture.PurposeId));
        await Assert.That((await fixture.ReleaseAsync(fixture.ReleaseIntent())).Outcome)
            .IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        await RequireUnreleasedAsync(fixture);
    }

    [Test]
    public async Task SafetyAuthorCannotIndependentlyApproveOwnAssertion()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = fixture.Intent(fixture.Author, ResourceSafetyReviewStep.ApproveSafety, fixture.EvidenceId);
        await Assert.That((await fixture.ReviewAsync(fixture.Author, request)).Outcome)
            .IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        await using var reader = fixture.Context();
        await Assert.That(await reader.Set<EventResourceModerationQuarantineReview>().AnyAsync(row => row.Id == request.OperationId)).IsFalse();
    }

    [Test]
    public async Task AdoptedTwoApprovalQuorumCannotBeReducedByTheReleaseCaller()
    {
        await using var fixture = await Fixture.CreateAsync(approvals: 2);
        await Assert.That((await fixture.ReleaseAsync(fixture.ReleaseIntent())).Outcome)
            .IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        var second = fixture.Intent(fixture.Database.AdministratorId, ResourceSafetyReviewStep.ApproveSafety, fixture.ApprovalId);
        await fixture.AcceptAsync(fixture.Database.AdministratorId, second);
        await using var context = fixture.Context();
        var identity = Identity(context, fixture.Database.AdministratorId);
        var result = await new ModerationResourceQuarantineReleaseRepository(context, identity.Admin, identity.Machine, fixture.Clock)
            .ReleaseAsync(fixture.ReleaseIntent() with { ApprovalId = second.OperationId });
        await Assert.That(result.Outcome).IsEqualTo(ModerationResourceSafetyOutcome.Known);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DeadlineAtEntryOrAfterNativeFlushLeavesCauseAndReceiptAtomic(bool afterFlush)
    {
        await using var fixture = await Fixture.CreateAsync();
        var interceptor = new ExpireAfterFlush(fixture.Clock, fixture.Deadline);
        if (!afterFlush) fixture.Clock.Now = fixture.Deadline;
        var result = afterFlush
            ? await fixture.ReleaseAsync(fixture.ReleaseIntent(), interceptor)
            : await fixture.ReleaseAsync(fixture.ReleaseIntent());
        await Assert.That(result.Outcome).IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        await Assert.That(interceptor.Observed).IsEqualTo(afterFlush);
        await RequireUnreleasedAsync(fixture);
    }

    [Test]
    public async Task ReviewDeadlineAfterFlushDoesNotPersistAFalseSafetyProof()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intent = fixture.Intent(fixture.Database.AdministratorId, ResourceSafetyReviewStep.ProposePurpose, null) with
        {
            RequiredApprovals = 1, MaximumEvidenceLifetime = TimeSpan.FromMinutes(1)
        };
        var interceptor = new ExpireAfterFlush(fixture.Clock, fixture.Deadline);
        await Assert.That((await fixture.ReviewAsync(fixture.Database.AdministratorId, intent, interceptor)).Outcome)
            .IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        await Assert.That(interceptor.Observed).IsTrue();
        await using var reader = fixture.Context();
        await Assert.That(await reader.Set<EventResourceModerationQuarantineReview>().AnyAsync(row => row.Id == intent.OperationId)).IsFalse();
    }

    [Test]
    public async Task MissingNativeControllerMutationProofCannotBecomePermission()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var writer = fixture.Context())
        {
            writer.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            await writer.ModerationQualificationMutationReceipts
                .Where(row => row.SourceId == fixture.Assignments[fixture.Approver] && row.ResultRevision == 2)
                .ExecuteDeleteAsync();
        }
        await Assert.That((await fixture.ReleaseAsync(fixture.ReleaseIntent())).Outcome)
            .IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        await RequireUnreleasedAsync(fixture);
    }

    private static async Task RequireUnreleasedAsync(Fixture fixture)
    {
        await using var reader = fixture.Context();
        var resource = await reader.EventResources.SingleAsync(row => row.Id == fixture.ResourceId);
        await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(2);
        await Assert.That(resource.HasPublishablePayload()).IsFalse();
        await Assert.That((await reader.EventResourceModerationQuarantines.SingleAsync(row => row.Id == fixture.CauseId)).ReleaseId).IsNull();
        await Assert.That(await reader.Set<EventResourceModerationQuarantineRelease>().CountAsync()).IsEqualTo(0);
    }
}
