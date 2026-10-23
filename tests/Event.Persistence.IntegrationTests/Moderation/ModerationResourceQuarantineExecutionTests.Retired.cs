using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceQuarantineExecutionTests
{
    [Test]
    public async Task OriginalApplyCannotReactivateAnExactlyRetiredCommittedCause()
    {
        await using var fixture = await Fixture.CreateAsync();
        var applied = await fixture.ExecuteAsync();
        await Assert.That(applied.Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Applied);
        var cause = applied.Receipt!;
        var approval = new EventResourceModerationQuarantineReview
        {
            Id = Guid.CreateVersion7(),
            TenantId = cause.TenantId,
            HostBindingId = fixture.Database.HostId,
            TrustDomainId = fixture.Database.Scope.TrustDomainId,
            QuarantineId = cause.Id,
            EventId = cause.EventId,
            EventResourceId = cause.EventResourceId,
            MaterialVersionId = cause.MaterialVersionId,
            CaseId = Guid.CreateVersion7(),
            CaseRevision = 1,
            Step = ResourceSafetyReviewStep.ApproveSafety,
            PurposeId = Guid.CreateVersion7(),
            PredecessorId = Guid.CreateVersion7(),
            HumanId = fixture.Database.AdministratorId,
            ReviewerAssignmentId = Guid.CreateVersion7(),
            ReviewerRevision = 3,
            ControllerHumanId = fixture.Database.AdministratorId,
            RequiredApprovals = 1,
            MaximumEvidenceLifetimeTicks = TimeSpan.FromMinutes(1).Ticks,
            Attestation = null,
            ResourceStamp = Guid.NewGuid(),
            NativeDigest = new string('A', 64),
            RequestDigest = new string('B', 64),
            QualificationRevision = 1,
            RecordedAt = Now,
            ValidUntil = Now.AddMinutes(1)
        };
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
            ApprovalId = approval.Id,
            ReleasedByHumanId = fixture.Database.AdministratorId,
            ReleasedAt = Now,
            RequestDigest = new string('A', 64)
        };
        await using (var writer = fixture.Context())
        {
            var resource = await writer.EventResources.Include(row => row.ModerationQuarantines)
                .SingleAsync(row => row.Id == fixture.ResourceId);
            // Seed committed operational retirement for the original executor boundary.
            // Fresh human review/approval conformance belongs to the release owner's witnesses.
            resource.ReleaseModerationQuarantine(release, resource.ConcurrencyStamp);
            writer.Set<EventResourceModerationQuarantineReview>().Add(approval);
            writer.Set<EventResourceModerationQuarantineRelease>().Add(release);
            await writer.SaveChangesAsync();
        }
        var replay = await fixture.ExecuteAsync();
        await Assert.That(replay.Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Retired);
        await Assert.That(replay.Receipt!.Id).IsEqualTo(cause.Id);
        await Assert.That(replay.Receipt.ReleaseId).IsEqualTo(release.Id);
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That((await reader.EventResources.SingleAsync(row => row.Id == fixture.ResourceId))
            .ModerationQuarantineCount).IsEqualTo(0);
        await Assert.That(await reader.EventResourceModerationQuarantines.CountAsync()).IsEqualTo(1);
        await Assert.That((await reader.ModerationEffectIntents.SingleAsync(row => row.Id == fixture.EffectId))
            .CompletedAt).IsNull();
    }
}
