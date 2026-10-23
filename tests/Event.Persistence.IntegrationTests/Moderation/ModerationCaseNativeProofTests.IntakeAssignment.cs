using Explore.Application.Contracts.Persistence;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    [Arguments("open")]
    [Arguments("closed")]
    [Arguments("stale")]
    public async Task NativeTargetOwnedIntakeAssignmentPreservesUnattributedCaseAndRejectsClosedOrStaleIntent(
        string state)
    {
        await using var fixture = await PreFindingFixture.CreateAsync(issueNotice: false);
        var database = fixture.Database;
        ModerationCase root;
        int attributions;
        await using (var seed = database.Context())
        {
            root = await seed.ModerationCases.SingleAsync(row => row.Id == fixture.Review.RootId);
            await Assert.That(root.Subject).IsNull();
            await Assert.That(root.Target).IsNotNull();
            if (state == "closed")
            {
                root.Close(root.Revision, database.Clock.Now);
                await seed.SaveChangesAsync();
            }
            attributions = await seed.ModerationCaseAttributionSources.CountAsync();
        }
        var assignmentId = Guid.CreateVersion7();
        await using var context = database.Context();
        var epoch = await context.ModerationQualificationEpochs.AsNoTracking().SingleAsync();
        context.ChangeTracker.Clear();
        var result = await fixture.Repository(context, fixture.Assessor).ExecuteAsync(
            new AssignModerationReviewerSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = root.Scope,
                ExpectedQualificationRevision = epoch.Revision,
                AssignmentId = assignmentId,
                CaseId = root.Id,
                ExpectedCaseRevision = root.Revision + (state == "stale" ? 1 : 0),
                ReviewerHumanId = fixture.Review.Caller.UserId!.Value
            }, default);

        await Assert.That(result.Outcome).IsEqualTo(state == "open"
            ? ModerationQualificationWriteOutcome.Known : ModerationQualificationWriteOutcome.Conflict);
        await using var inspect = database.Context();
        var retained = await inspect.ModerationCases.AsNoTracking().SingleAsync(row => row.Id == root.Id);
        await Assert.That(retained.Subject).IsNull();
        await Assert.That(retained.Target).IsEqualTo(root.Target);
        await Assert.That(retained.State).IsEqualTo(root.State);
        await Assert.That(retained.Revision).IsEqualTo(root.Revision);
        await Assert.That(await inspect.ModerationCaseAttributionSources.CountAsync()).IsEqualTo(attributions);
        var assignment = await inspect.ModerationReviewerAssignments.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == assignmentId);
        if (state != "open")
        {
            await Assert.That(assignment).IsNull();
            return;
        }
        await Assert.That(assignment).IsNotNull();
        await Assert.That(assignment!.CaseId).IsEqualTo(root.Id);
        await Assert.That(assignment.SubjectHumanId).IsNull();
        await Assert.That(assignment.ConflictState).IsEqualTo(ModerationReviewerConflictState.Unresolved);
        await Assert.That(assignment.IsQualifiedAt(database.Clock.Now)).IsFalse();

        await using (var qualify = database.Context())
        {
            var current = await qualify.ModerationQualificationEpochs.AsNoTracking().SingleAsync();
            var rejected = await fixture.Repository(qualify, fixture.Assessor).ExecuteAsync(
                new QualifyModerationReviewerController
                {
                    OperationId = Guid.CreateVersion7(),
                    Target = root.Scope,
                    ExpectedQualificationRevision = current.Revision,
                    AssignmentId = assignmentId,
                    ExpectedAssignmentRevision = 1,
                    ControllerHumanId = assignment.ReviewerHumanId,
                    EvidenceId = Guid.CreateVersion7(),
                    Justification = "Triage is not responsibility attribution.",
                    QualifiedUntil = database.Clock.Now.AddSeconds(10)
                }, default);
            await Assert.That(rejected.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Denied);
        }
        await using (var withdraw = database.Context())
        {
            var current = await withdraw.ModerationQualificationEpochs.AsNoTracking().SingleAsync();
            var withdrawn = await fixture.Repository(withdraw, fixture.Assessor).ExecuteAsync(
                new WithdrawModerationReviewerSource
                {
                    OperationId = Guid.CreateVersion7(),
                    Target = root.Scope,
                    ExpectedQualificationRevision = current.Revision,
                    AssignmentId = assignmentId,
                    ExpectedAssignmentRevision = 1
                }, default);
            await Assert.That(withdrawn.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        }
        await using var final = database.Context();
        var removed = await final.ModerationReviewerAssignments.AsNoTracking()
            .SingleAsync(row => row.Id == assignmentId);
        await Assert.That(removed.WithdrawnByHumanId).IsEqualTo(fixture.Assessor);
        await Assert.That(removed.Revision).IsEqualTo(2L);
        await Assert.That(removed.ControllerHumanId).IsNull();
        await Assert.That(removed.IsQualifiedAt(database.Clock.Now)).IsFalse();
    }
}
