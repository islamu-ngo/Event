namespace Event.Domain.UnitTests.Entities;

public sealed class EventResourceModerationReviewDutyTests
{
    private static readonly DateTimeOffset Now = new(EventResourceTestData.Now);

    [Test]
    public async Task ReviewIsDueAtTwentyFourHoursAndNeverReleasesMaterial()
    {
        var (resource, _, _) = EventResourceTestData.CreatePublished();
        var cause = resource.ApplyModerationQuarantine(Guid.CreateVersion7(), Guid.CreateVersion7(), 0,
            resource.MaterialVersionId!.Value, resource.ConcurrencyStamp, Now);
        var duty = EventResourceModerationReviewDuty.Discover(cause, Now.AddHours(24));
        await Assert.That(duty.IsOverdueAt(Now.AddHours(24).AddTicks(-1), null)).IsFalse();
        await Assert.That(duty.IsOverdueAt(Now.AddHours(24), null)).IsTrue();
        await Assert.That(duty.IsOverdueAt(Now.AddYears(1), null)).IsTrue();
        await Assert.That(duty.Owns(cause)).IsTrue();
        await Assert.That(cause.ReleaseId).IsNull();
        await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(1);
        await Assert.That(resource.HasPublishablePayload()).IsFalse();
        await Assert.That(() => EventResourceModerationReviewDuty.Discover(cause, Now.AddHours(24).AddTicks(1)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task EarlierChosenDeadlineIsNotReplacedByFullDayAllowance()
    {
        var (resource, _, _) = EventResourceTestData.CreatePublished();
        var cause = resource.ApplyModerationQuarantine(Guid.CreateVersion7(), Guid.CreateVersion7(), 0,
            resource.MaterialVersionId!.Value, resource.ConcurrencyStamp, Now);
        var duty = EventResourceModerationReviewDuty.Discover(cause, Now.AddHours(1));
        await Assert.That(duty.IsOverdueAt(Now.AddHours(1), null)).IsTrue();
        await Assert.That(duty.DueAt).IsEqualTo(Now.AddHours(1));
        await Assert.That(() => ((ITenantEntity)duty).TenantId = Guid.CreateVersion7()).Throws<InvalidOperationException>();
    }
    [Test]
    public async Task FiniteDeadlineNearCalendarLimitDoesNotOverflow()
    {
        var (resource, _, _) = EventResourceTestData.CreatePublished();
        var appliedAt = DateTimeOffset.MaxValue.AddHours(-2);
        var cause = resource.ApplyModerationQuarantine(Guid.CreateVersion7(), Guid.CreateVersion7(), 0,
            resource.MaterialVersionId!.Value, resource.ConcurrencyStamp, appliedAt);
        var dueAt = DateTimeOffset.MaxValue.AddHours(-1);
        var duty = EventResourceModerationReviewDuty.Discover(cause, dueAt);
        await Assert.That(duty.DueAt).IsEqualTo(dueAt);
        await Assert.That(duty.IsOverdueAt(dueAt, null)).IsTrue();
        await Assert.That(cause.ReleaseId).IsNull();
    }

    [Test]
    [Arguments(1, false)]
    [Arguments(1, true)]
    [Arguments(2, false)]
    [Arguments(2, true)]
    public async Task IndependentCompletionDischargesDutyWithoutReleasingCause(int approvals, bool unsafeMaterial)
    {
        var (resource, _, _) = EventResourceTestData.CreatePublished();
        var cause = resource.ApplyModerationQuarantine(Guid.CreateVersion7(), Guid.CreateVersion7(), 0,
            resource.MaterialVersionId!.Value, resource.ConcurrencyStamp, Now);
        var chain = Chain(cause, approvals, unsafeMaterial);
        var completion = EventResourceModerationReviewCompletion.Complete(cause, chain);
        var duty = EventResourceModerationReviewDuty.Discover(cause, Now.AddHours(24));
        await Assert.That(completion.Owns(cause)).IsTrue();
        await Assert.That(completion.ApprovalId).IsEqualTo(chain[^1].Id);
        await Assert.That(completion.Disposition).IsEqualTo(chain[2].Attestation!.Value);
        await Assert.That(duty.IsOverdueAt(Now.AddDays(2), completion)).IsFalse();
        await Assert.That(cause.ReleaseId).IsNull();
        await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(1);
        await Assert.That(resource.HasPublishablePayload()).IsFalse();
    }

    [Test]
    [Arguments("host")]
    [Arguments("trust")]
    [Arguments("case")]
    [Arguments("case-revision")]
    [Arguments("stamp")]
    [Arguments("evidence-lifetime")]
    [Arguments("backward-time")]
    [Arguments("extended-validity")]
    [Arguments("duplicate-identity")]
    public async Task MixedOrReorderedReviewCannotCompleteDuty(string defect)
    {
        var (resource, _, _) = EventResourceTestData.CreatePublished();
        var cause = resource.ApplyModerationQuarantine(Guid.CreateVersion7(), Guid.CreateVersion7(), 0,
            resource.MaterialVersionId!.Value, resource.ConcurrencyStamp, Now);
        var chain = Chain(cause, 2, false, defect);
        await Assert.That(() => EventResourceModerationReviewCompletion.Complete(cause, chain))
            .Throws<ArgumentException>();
        var duty = EventResourceModerationReviewDuty.Discover(cause, Now.AddHours(24));
        await Assert.That(duty.IsOverdueAt(Now.AddHours(24), null)).IsTrue();
        await Assert.That(cause.ReleaseId).IsNull();
        await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(1);
    }

    [Test]
    [Arguments("human")]
    [Arguments("controller")]
    [Arguments("cross-controller")]
    public async Task SharedInspectionAuthorityCannotApproveItsOwnDisposition(string defect)
    {
        var (resource, _, _) = EventResourceTestData.CreatePublished();
        var cause = resource.ApplyModerationQuarantine(Guid.CreateVersion7(), Guid.CreateVersion7(), 0,
            resource.MaterialVersionId!.Value, resource.ConcurrencyStamp, Now);
        await Assert.That(() => EventResourceModerationReviewCompletion.Complete(cause, Chain(cause, 2, false, defect)))
            .Throws<ArgumentException>();
        await Assert.That(cause.ReleaseId).IsNull();
    }

    [Test]
    public async Task ReviewPredecessorRequiresNativeVersionSevenIdentity()
    {
        var (resource, _, _) = EventResourceTestData.CreatePublished();
        var cause = resource.ApplyModerationQuarantine(Guid.CreateVersion7(), Guid.CreateVersion7(), 0,
            resource.MaterialVersionId!.Value, resource.ConcurrencyStamp, Now);
        var chain = Chain(cause, 1, false, "predecessor-version");
        await Assert.That(() => chain[1].ValidatePersistedState()).Throws<ArgumentException>();
    }

    private static IReadOnlyList<EventResourceModerationQuarantineReview> Chain(
        EventResourceModerationQuarantine cause, int approvals, bool unsafeMaterial, string? defect = null)
    {
        var rows = new List<EventResourceModerationQuarantineReview>();
        var purpose = Guid.CreateVersion7();
        var host = Guid.CreateVersion7();
        var trust = Guid.CreateVersion7();
        var caseId = Guid.CreateVersion7();
        var stamp = Guid.CreateVersion7();
        for (var index = 0; index < 3 + approvals; index++)
        {
            rows.Add(new EventResourceModerationQuarantineReview
            {
                Id = index == 0 ? purpose : index == 4 && defect == "duplicate-identity" ? rows[2].Id : Guid.CreateVersion7(),
                TenantId = cause.TenantId,
                QuarantineId = cause.Id,
                EventId = cause.EventId,
                EventResourceId = cause.EventResourceId,
                MaterialVersionId = cause.MaterialVersionId,
                HostBindingId = index == 3 && defect == "host" ? Guid.CreateVersion7() : host,
                TrustDomainId = index == 3 && defect == "trust" ? Guid.CreateVersion7() : trust,
                CaseId = index == 3 && defect == "case" ? Guid.CreateVersion7() : caseId,
                CaseRevision = index == 3 && defect == "case-revision" ? 2 : 1,
                ResourceStamp = index == 3 && defect == "stamp" ? Guid.CreateVersion7() : stamp,
                Step = index switch
                {
                    0 => ResourceSafetyReviewStep.ProposePurpose,
                    1 => ResourceSafetyReviewStep.AdoptPurpose,
                    2 => ResourceSafetyReviewStep.AttestMaterialSafety,
                    _ => unsafeMaterial ? ResourceSafetyReviewStep.ApproveContinuedQuarantine : ResourceSafetyReviewStep.ApproveSafety
                },
                PurposeId = purpose,
                PredecessorId = index == 0 ? null : index == 1 && defect == "predecessor-version"
                    ? Guid.Parse("00000000-0000-4000-8000-000000000001") : rows[index - 1].Id,
                HumanId = index == 3 && defect == "human" ? rows[2].HumanId
                    : index == 3 && defect == "cross-controller" ? rows[2].ControllerHumanId : Guid.CreateVersion7(),
                ControllerHumanId = index == 3 && defect == "controller" ? rows[2].ControllerHumanId : Guid.CreateVersion7(),
                ReviewerAssignmentId = Guid.CreateVersion7(),
                ReviewerRevision = 3,
                QualificationRevision = 1,
                RequiredApprovals = approvals,
                MaximumEvidenceLifetimeTicks = TimeSpan.FromHours(index == 3 && defect == "evidence-lifetime" ? 2 : 1).Ticks,
                Attestation = index != 2 ? null : unsafeMaterial
                    ? ResourceSafetyAttestation.IndependentlyInspectedUnsafeForCurrentAudience
                    : ResourceSafetyAttestation.IndependentlyInspectedSafeForCurrentAudience,
                NativeDigest = new string('A', 64),
                RequestDigest = new string('B', 64),
                RecordedAt = Now.AddMinutes(index == 1 && defect == "backward-time" ? 3 : index),
                ValidUntil = Now.AddHours(index == 1 && defect == "extended-validity" ? 2 : 1)
            });
        }
        return rows;
    }
}
