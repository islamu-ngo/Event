using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceQuarantineReleaseTests
{
    [Test]
    public async Task CurrentReviewerDiscoversExactOperationalCausesUnderTerminalFencesWithoutInspection()
    {
        await using var fixture = await InspectionFixture.CreateAsync();
        await using var session = fixture.Session();
        var pending = await session.PendingStateAsync(StateIntent(fixture));
        var outputs = 0;
        await Assert.That(await pending.DiscloseAsync(async (page, current, token) =>
        {
            await Assert.That(session.Context.Database.CurrentTransaction).IsNotNull();
            await Assert.That(session.Context.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel)
                .IsEqualTo(System.Data.IsolationLevel.Serializable);
            await Assert.That(await current(token)).IsTrue();
            await Assert.That(page.ResourceId).IsEqualTo(fixture.Native.ResourceId);
            await Assert.That(page.Items.Count).IsEqualTo(2);
            await Assert.That(page.Items.Any(row => row.QuarantineId == fixture.Native.CauseId)).IsTrue();
            await Assert.That(page.Items.All(row => row.MaterialVersionId == fixture.Native.MaterialId
                && row.ReleaseId is null && row.ReviewedAt is null)).IsTrue();
            outputs++;
            return true;
        }, default)).IsTrue();
        await Assert.That(outputs).IsEqualTo(1);
        await Assert.That(await pending.DiscloseAsync((_, _, _) =>
            throw new InvalidOperationException("A pending state read cannot be reused."), default)).IsFalse();
        await Assert.That(session.Context.Database.CurrentTransaction).IsNull();
        await Assert.That(session.Context.ModerationTrustDomainId).IsNull();
        await Assert.That(session.Context.ChangeTracker.Entries().Any()).IsFalse();
    }

    [Test]
    [Arguments("resource")]
    [Arguments("material")]
    [Arguments("case")]
    [Arguments("case-revision")]
    [Arguments("reviewer")]
    [Arguments("reviewer-revision")]
    [Arguments("epoch")]
    [Arguments("page-zero")]
    [Arguments("page-overflow")]
    public async Task StateDiscoveryCannotBorrowForeignOrStaleCoordinates(string substitution)
    {
        await using var fixture = await InspectionFixture.CreateAsync();
        await using var session = fixture.Session();
        var intent = StateIntent(fixture);
        intent = substitution switch
        {
            "resource" => intent with { ResourceId = Guid.CreateVersion7() },
            "material" => intent with { MaterialVersionId = Guid.CreateVersion7() },
            "case" => intent with { CaseId = Guid.CreateVersion7() },
            "case-revision" => intent with { ExpectedCaseRevision = intent.ExpectedCaseRevision + 1 },
            "reviewer" => intent with { ReviewerAssignmentId = Guid.CreateVersion7() },
            "reviewer-revision" => intent with { ExpectedReviewerRevision = intent.ExpectedReviewerRevision + 1 },
            "epoch" => intent with { ExpectedQualificationRevision = intent.ExpectedQualificationRevision + 1 },
            "page-zero" => intent with { Page = 0 },
            "page-overflow" => intent with { Page = int.MaxValue },
            _ => throw new ArgumentOutOfRangeException(nameof(substitution))
        };
        var outputs = 0;
        var pending = await session.PendingStateAsync(intent);
        await Assert.That(await pending.DiscloseAsync((_, _, _) =>
        {
            outputs++;
            return Task.FromResult(true);
        }, default)).IsFalse();
        await Assert.That(outputs).IsEqualTo(0);
    }

    [Test]
    public async Task NativeAdministratorCannotBorrowAnotherHumansReviewerAssignment()
    {
        await using var fixture = await InspectionFixture.CreateAsync();
        await using var session = fixture.Session(fixture.Native.Approver);
        var pending = await session.PendingStateAsync(StateIntent(fixture));
        await Assert.That(await pending.DiscloseAsync((_, _, _) =>
            throw new InvalidOperationException("Administrative role is not the named reviewer."), default)).IsFalse();
    }

    [Test]
    public async Task RetainedOwnerErasureBlocksOperationalMetadataBeforeOutput()
    {
        await using var fixture = await InspectionFixture.CreateAsync();
        await fixture.Retained().AppendAsync(PrivacyErasureRequest.Create(Guid.CreateVersion7(),
            PrivacyErasureSubjectKind.User, fixture.Native.Database.SubjectId,
            PrivacyErasureReasonCode.SubjectErasureRequest, 1));
        await using var session = fixture.Session();
        var pending = await session.PendingStateAsync(StateIntent(fixture));
        await Assert.That(await pending.DiscloseAsync((_, _, _) =>
            throw new InvalidOperationException("Retained erasure must prevent metadata output."), default)).IsFalse();
    }

    [Test]
    public async Task ActualSerializationRechecksExpiryBeforeAnyMetadataBytes()
    {
        await using var fixture = await InspectionFixture.CreateAsync();
        await using var session = fixture.Session();
        var pending = await session.PendingStateAsync(StateIntent(fixture));
        var outputs = 0;
        await Assert.That(await pending.DiscloseAsync(async (_, current, token) =>
        {
            fixture.Native.Clock.Now = fixture.Native.Clock.Now.AddDays(2);
            if (!await current(token)) return false;
            outputs++;
            return true;
        }, default)).IsFalse();
        await Assert.That(outputs).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SuccessiveCompletedReviewsPreserveHistoryAndExposeTheLatestDisposition(bool sameInstant)
    {
        await using var fixture = await InspectionFixture.CreateAsync();
        var native = fixture.Native;
        Guid adoptionId;
        await using (var context = native.Context())
            adoptionId = await context.Set<EventResourceModerationQuarantineReview>()
                .Where(row => row.PurposeId == fixture.PurposeId && row.Step == ResourceSafetyReviewStep.AdoptPurpose)
                .Select(row => row.Id).SingleAsync();
        var unsafeInspection = native.Intent(native.Author, ResourceSafetyReviewStep.AttestMaterialSafety, adoptionId) with
        {
            Attestation = ResourceSafetyAttestation.IndependentlyInspectedUnsafeForCurrentAudience
        };
        await native.AcceptAsync(native.Author, unsafeInspection);
        var unsafeApproval = native.Intent(native.Approver, ResourceSafetyReviewStep.ApproveContinuedQuarantine,
            unsafeInspection.OperationId);
        await native.AcceptAsync(native.Approver, unsafeApproval);
        if (!sameInstant) native.Clock.Now = native.Clock.Now.AddSeconds(1);
        await native.PrepareAsync(native.CauseId);

        EventResourceModerationReviewCompletion[] history;
        await using (var context = native.Context())
            history = await context.Set<EventResourceModerationReviewCompletion>().AsNoTracking()
                .Where(row => row.QuarantineId == native.CauseId).ToArrayAsync();
        await Assert.That(history.Length).IsEqualTo(2);
        await Assert.That(history.Any(row => row.Id == unsafeApproval.OperationId
            && row.Disposition == ResourceSafetyAttestation.IndependentlyInspectedUnsafeForCurrentAudience)).IsTrue();
        await Assert.That(history.Any(row => row.Id == native.ApprovalId
            && row.Disposition == ResourceSafetyAttestation.IndependentlyInspectedSafeForCurrentAudience)).IsTrue();
        var expected = sameInstant ? history.MaxBy(row => row.Id)! : history.Single(row => row.Id == native.ApprovalId);
        await Assert.That(history.Select(row => row.CompletedAt).Distinct().Count()).IsEqualTo(sameInstant ? 1 : 2);

        await using var session = fixture.Session();
        var pending = await session.PendingStateAsync(StateIntent(fixture));
        await Assert.That(await pending.DiscloseAsync(async (page, current, token) =>
        {
            await Assert.That(await current(token)).IsTrue();
            var state = page.Items.Single(row => row.QuarantineId == native.CauseId);
            await Assert.That(state.ReviewedAt).IsEqualTo(expected.CompletedAt);
            await Assert.That(state.Disposition).IsEqualTo(expected.Disposition);
            await Assert.That(state.ReleaseId).IsNull();
            await Assert.That(page.Items.Count).IsEqualTo(2);
            return true;
        }, default)).IsTrue();
        await RequireUnreleasedAsync(native);
        await using var retained = native.Context();
        await Assert.That(await retained.Set<EventResourceModerationReviewCompletion>()
            .CountAsync(row => row.QuarantineId == native.CauseId)).IsEqualTo(2);
    }

    private static ModerationResourceQuarantineStateIntent StateIntent(InspectionFixture fixture) => new(
        fixture.Native.ResourceId, fixture.Native.MaterialId, fixture.Intent.CaseId,
        fixture.Intent.ExpectedCaseRevision, fixture.Intent.ReviewerAssignmentId,
        fixture.Intent.ExpectedReviewerRevision, fixture.Intent.ExpectedQualificationRevision);
}
