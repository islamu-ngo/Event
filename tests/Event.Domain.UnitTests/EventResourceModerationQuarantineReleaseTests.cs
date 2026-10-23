using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Entities;

public sealed class EventResourceModerationQuarantineReleaseTests
{
    private static readonly DateTimeOffset Now = new(EventResourceTestData.Now);

    [Test]
    public async Task ExactReleaseRetiresOnlyItsCauseAndPreservesOriginalCustody()
    {
        var (resource, _, _) = EventResourceTestData.CreatePublished();
        var first = Apply(resource);
        var second = Apply(resource);
        var receipt = Receipt(first);
        resource.ReleaseModerationQuarantine(receipt, resource.ConcurrencyStamp);
        await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(1);
        await Assert.That(resource.ModerationQuarantines.Single().Id).IsEqualTo(second.Id);
        await Assert.That(resource.HasPublishablePayload()).IsFalse();
        await Assert.That(first.ReleaseId).IsEqualTo(receipt.Id);
        await Assert.That(first.EffectId).IsEqualTo(receipt.OriginalEffectId);
        await Assert.That(first.AppliedAt).IsEqualTo(Now);
        await Assert.That(resource.PublicationStateId).IsEqualTo((int)EventResourcePublicationStateEnum.Published);
        resource.ReleaseModerationQuarantine(receipt, Guid.NewGuid());
        await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(1);
        resource.ReleaseModerationQuarantine(Receipt(second), resource.ConcurrencyStamp);
        await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(0);
        await Assert.That(resource.HasPublishablePayload()).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReleaseNeverRestoresDeletedOrDiscardedMaterial(bool parentRedaction)
    {
        var (resource, _, actor) = EventResourceTestData.CreatePublished();
        var cause = Apply(resource);
        if (parentRedaction) resource.ApplyParentModeration("Unavailable", null, Now.UtcDateTime);
        else resource.Delete(resource.ConcurrencyStamp, actor, Now.UtcDateTime);
        await Assert.That(() => resource.ReleaseModerationQuarantine(Receipt(cause), resource.ConcurrencyStamp))
            .Throws<InvalidOperationException>();
        await Assert.That(resource.StorageObjectId).IsNull();
        await Assert.That(resource.ExternalDestinationCiphertext).IsNull();
        await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(1);
        await Assert.That(cause.ReleaseId).IsNull();
    }

    [Test]
    public async Task ASecondReleaseIdentityCannotSpendTheSameCauseAgain()
    {
        var (resource, _, _) = EventResourceTestData.CreatePublished();
        var first = Apply(resource);
        Apply(resource);
        resource.ReleaseModerationQuarantine(Receipt(first), resource.ConcurrencyStamp);
        await Assert.That(() => resource.ReleaseModerationQuarantine(Receipt(first), resource.ConcurrencyStamp))
            .Throws<InvalidOperationException>();
        await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(1);
    }

    [Test]
    public async Task CrossedMaterialAndStaleStampDoNotChangeGuardOrCause()
    {
        var (resource, _, _) = EventResourceTestData.CreatePublished();
        var cause = Apply(resource);
        await Assert.That(() => resource.ReleaseModerationQuarantine(Receipt(cause, Guid.CreateVersion7()),
            resource.ConcurrencyStamp)).Throws<InvalidOperationException>();
        await Assert.That(() => resource.ReleaseModerationQuarantine(Receipt(cause), Guid.NewGuid()))
            .Throws<InvalidOperationException>();
        await Assert.That(cause.ReleaseId).IsNull();
        await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(1);
    }

    [Test]
    public async Task RetirementDoesNotTransferToReplacementMaterialOrAnotherOrdinal()
    {
        var (resource, _, actor) = EventResourceTestData.CreatePublished();
        var original = Apply(resource);
        resource.ReleaseModerationQuarantine(Receipt(original), resource.ConcurrencyStamp);
        resource.SetStoredFile(Guid.CreateVersion7(), resource.ConcurrencyStamp, actor, Now.UtcDateTime);
        var replacement = Apply(resource);
        await Assert.That(replacement.MaterialVersionId).IsNotEqualTo(original.MaterialVersionId);
        await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(1);
        await Assert.That(resource.HasPublishablePayload()).IsFalse();
        await Assert.That(() => resource.ApplyModerationQuarantine(Guid.CreateVersion7(),
            original.EffectId, original.BindingOrdinal, replacement.MaterialVersionId,
            resource.ConcurrencyStamp, Now)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task DraftReleaseLeavesPublicationToItsActualOwner()
    {
        var (resource, _, actor) = EventResourceTestData.CreateDraft();
        resource.SetStoredFile(Guid.CreateVersion7(), resource.ConcurrencyStamp, actor, Now.UtcDateTime);
        var cause = Apply(resource);
        resource.ReleaseModerationQuarantine(Receipt(cause), resource.ConcurrencyStamp);
        await Assert.That(resource.HasPublishablePayload()).IsTrue();
        await Assert.That(resource.PublicationStateId).IsEqualTo((int)EventResourcePublicationStateEnum.Draft);
    }

    private static EventResourceModerationQuarantine Apply(EventResource resource) =>
        resource.ApplyModerationQuarantine(Guid.CreateVersion7(), Guid.CreateVersion7(), 0,
            resource.MaterialVersionId!.Value, resource.ConcurrencyStamp, Now);

    private static EventResourceModerationQuarantineRelease Receipt(
        EventResourceModerationQuarantine cause, Guid? material = null) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = cause.TenantId,
            QuarantineId = cause.Id,
            EventId = cause.EventId,
            EventResourceId = cause.EventResourceId,
            MaterialVersionId = material ?? cause.MaterialVersionId,
            OriginalEffectId = cause.EffectId,
            OriginalBindingOrdinal = cause.BindingOrdinal,
            OriginallyAppliedAt = cause.AppliedAt,
            ApprovalId = Guid.CreateVersion7(),
            ReleasedByHumanId = Guid.CreateVersion7(),
            ReleasedAt = Now.AddDays(2),
            RequestDigest = new string('A', 64)
        };
}
