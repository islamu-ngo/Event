using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceQuarantineReleaseTests
{
    [Test]
    [Arguments("author", true)]
    [Arguments("adopter", true)]
    [Arguments("independent", false)]
    public async Task ActualAdoptedPurposeOffersInspectionOnlyToItsCurrentParticipants(string caller, bool canInspect)
    {
        await using var fixture = await InspectionFixture.CreateAsync();
        var human = caller switch
        {
            "author" => fixture.Native.Author,
            "adopter" => fixture.Native.Approver,
            "independent" => fixture.Native.Database.AdministratorId,
            _ => throw new ArgumentOutOfRangeException(nameof(caller))
        };
        await using var session = fixture.Session(human);
        var pending = await session.PendingReviewStateAsync(ReviewStateIntent(fixture, fixture.Intent.AdoptionId, human));
        var outputs = 0;
        await Assert.That(await pending.DiscloseAsync(async (state, current, token) =>
        {
            await Assert.That(await current(token)).IsTrue();
            await Assert.That(state.Chain.Count).IsEqualTo(2);
            await Assert.That(state.AdoptionId).IsEqualTo(fixture.Intent.AdoptionId);
            await Assert.That(state.CanInspect).IsEqualTo(canInspect);
            await Assert.That(state.CanRelease).IsFalse();
            await Assert.That(state.AllowedSteps).Contains(ResourceSafetyReviewStep.AttestMaterialSafety);
            using var wire = JsonDocument.Parse(JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            await Assert.That(wire.RootElement.TryGetProperty("canInspect", out _)).IsFalse();
            await Assert.That(wire.RootElement.TryGetProperty("canRelease", out _)).IsFalse();
            await Assert.That(wire.RootElement.TryGetProperty("allowedSteps", out _)).IsFalse();
            outputs++;
            return true;
        }, default)).IsTrue();
        await Assert.That(outputs).IsEqualTo(1);
        await Assert.That(await pending.DiscloseAsync((_, _, _) =>
            throw new InvalidOperationException("Review-state disclosure cannot be retried."), default)).IsFalse();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task SafeAndUnsafeQuorumHaveDifferentCurrentActions(bool unsafeMaterial, bool complete)
    {
        await using var fixture = await InspectionFixture.CreateAsync();
        var evidence = fixture.Native.Intent(fixture.Native.Author, ResourceSafetyReviewStep.AttestMaterialSafety,
            fixture.Intent.AdoptionId) with
        {
            Attestation = unsafeMaterial
                ? ResourceSafetyAttestation.IndependentlyInspectedUnsafeForCurrentAudience
                : ResourceSafetyAttestation.IndependentlyInspectedSafeForCurrentAudience
        };
        await fixture.Native.AcceptAsync(fixture.Native.Author, evidence);
        var tip = evidence.OperationId;
        var step = unsafeMaterial ? ResourceSafetyReviewStep.ApproveContinuedQuarantine : ResourceSafetyReviewStep.ApproveSafety;
        if (complete)
        {
            var approval = fixture.Native.Intent(fixture.Native.Approver, step, tip);
            await fixture.Native.AcceptAsync(fixture.Native.Approver, approval);
            tip = approval.OperationId;
        }
        await using var session = fixture.Session(fixture.Native.Approver);
        var pending = await session.PendingReviewStateAsync(ReviewStateIntent(fixture, tip, fixture.Native.Approver));
        await Assert.That(await pending.DiscloseAsync(async (state, current, token) =>
        {
            await Assert.That(await current(token)).IsTrue();
            await Assert.That(state.CanRelease).IsEqualTo(complete && !unsafeMaterial);
            await Assert.That(state.AllowedSteps.Contains(step)).IsEqualTo(!complete);
            await Assert.That(state.AllowedSteps.Contains(unsafeMaterial
                ? ResourceSafetyReviewStep.ApproveSafety : ResourceSafetyReviewStep.ApproveContinuedQuarantine)).IsFalse();
            return true;
        }, default)).IsTrue();
    }

    [Test]
    public async Task AnotherParticipantCannotBorrowTheFinalApproversReleaseAffordance()
    {
        await using var fixture = await InspectionFixture.CreateAsync();
        var evidence = fixture.Native.Intent(fixture.Native.Author, ResourceSafetyReviewStep.AttestMaterialSafety,
            fixture.Intent.AdoptionId) with { Attestation = ResourceSafetyAttestation.IndependentlyInspectedSafeForCurrentAudience };
        await fixture.Native.AcceptAsync(fixture.Native.Author, evidence);
        var approval = fixture.Native.Intent(fixture.Native.Approver, ResourceSafetyReviewStep.ApproveSafety, evidence.OperationId);
        await fixture.Native.AcceptAsync(fixture.Native.Approver, approval);
        await using var session = fixture.Session();
        var pending = await session.PendingReviewStateAsync(ReviewStateIntent(fixture, approval.OperationId, fixture.Native.Author));
        await Assert.That(await pending.DiscloseAsync(async (state, _, _) =>
        {
            await Assert.That(state.CanRelease).IsFalse();
            return !state.CanRelease;
        }, default)).IsTrue();
    }

    [Test]
    public async Task SupersededTipCannotAdvertiseAnEarlierStepOfTheSamePurpose()
    {
        await using var fixture = await InspectionFixture.CreateAsync();
        var evidence = fixture.Native.Intent(fixture.Native.Author, ResourceSafetyReviewStep.AttestMaterialSafety,
            fixture.Intent.AdoptionId) with { Attestation = ResourceSafetyAttestation.IndependentlyInspectedSafeForCurrentAudience };
        await fixture.Native.AcceptAsync(fixture.Native.Author, evidence);
        await using var session = fixture.Session();
        var pending = await session.PendingReviewStateAsync(ReviewStateIntent(fixture, fixture.Intent.AdoptionId, fixture.Native.Author));
        await Assert.That(await pending.DiscloseAsync((_, _, _) =>
            throw new InvalidOperationException("The actual native successor must prevent stale tip output."), default)).IsFalse();
    }

    [Test]
    [Arguments("cause")]
    [Arguments("material")]
    [Arguments("tip")]
    [Arguments("case")]
    [Arguments("epoch")]
    [Arguments("reviewer")]
    public async Task ReviewStateCannotBorrowForeignSourceCoordinates(string substitution)
    {
        await using var fixture = await InspectionFixture.CreateAsync();
        await using var session = fixture.Session();
        var intent = ReviewStateIntent(fixture, fixture.Intent.AdoptionId, fixture.Native.Author);
        intent = substitution switch
        {
            "cause" => intent with { QuarantineId = fixture.Native.OtherCauseId },
            "material" => intent with { MaterialVersionId = Guid.CreateVersion7() },
            "tip" => intent with { TipId = Guid.CreateVersion7() },
            "case" => intent with { CaseId = Guid.CreateVersion7() },
            "epoch" => intent with { ExpectedQualificationRevision = intent.ExpectedQualificationRevision + 1 },
            "reviewer" => intent with { ReviewerAssignmentId = fixture.Native.Assignments[fixture.Native.Approver] },
            _ => throw new ArgumentOutOfRangeException(nameof(substitution))
        };
        var pending = await session.PendingReviewStateAsync(intent);
        await Assert.That(await pending.DiscloseAsync((_, _, _) =>
            throw new InvalidOperationException("Foreign or stale source cannot disclose current purpose metadata."), default)).IsFalse();
    }

    [Test]
    public async Task RetainedPurposeParticipantErasurePreventsReviewMetadataAndActions()
    {
        await using var fixture = await InspectionFixture.CreateAsync();
        await fixture.Retained().AppendAsync(PrivacyErasureRequest.Create(Guid.CreateVersion7(),
            PrivacyErasureSubjectKind.User, fixture.Native.Approver, PrivacyErasureReasonCode.SubjectErasureRequest, 1));
        await using var session = fixture.Session();
        var pending = await session.PendingReviewStateAsync(ReviewStateIntent(fixture, fixture.Intent.AdoptionId, fixture.Native.Author));
        await Assert.That(await pending.DiscloseAsync((_, _, _) =>
            throw new InvalidOperationException("Retained participant erasure must prevent action metadata."), default)).IsFalse();
    }

    private static ModerationResourceQuarantineReviewStateIntent ReviewStateIntent(
        InspectionFixture fixture, Guid tip, Guid human) => new()
    {
        ResourceId = fixture.Native.ResourceId, MaterialVersionId = fixture.Native.MaterialId,
        QuarantineId = fixture.Native.CauseId, TipId = tip, CaseId = fixture.Native.CaseId,
        ExpectedCaseRevision = fixture.Intent.ExpectedCaseRevision,
        ReviewerAssignmentId = fixture.Native.Assignments[human],
        ExpectedReviewerRevision = fixture.Intent.ExpectedReviewerRevision,
        ExpectedQualificationRevision = fixture.Intent.ExpectedQualificationRevision
    };
}
