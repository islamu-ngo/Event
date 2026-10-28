using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Entities;

/// <summary>Native state witnesses; execution authority and release are separate, still-closed owners.</summary>
public sealed class EventResourceModerationQuarantineTests
{
    private static readonly DateTimeOffset Now = new(EventResourceTestData.Now);

    [Test]
    public async Task MaterialIdentityChangesWithDeliveryButNotMetadataOrConcurrencyStamp()
    {
        var (resource, _, actor) = EventResourceTestData.CreateDraft();
        await Assert.That(resource.MaterialVersionId).IsNull();
        var file = Guid.CreateVersion7();
        resource.SetStoredFile(file, resource.ConcurrencyStamp, actor, Now.UtcDateTime);
        var first = resource.MaterialVersionId;
        await Assert.That(first).IsNotNull();
        resource.SetStoredFile(file, resource.ConcurrencyStamp, actor, Now.UtcDateTime);
        resource.UpdateMetadata(EventResourceTestData.Metadata with { Title = "Corrected title" },
            resource.ConcurrencyStamp, actor, Now.UtcDateTime);
        resource.ConcurrencyStamp = Guid.NewGuid();
        await Assert.That(resource.MaterialVersionId).IsEqualTo(first);
        resource.SetExternalDestination("protected-envelope", 1, "https://material.example",
            resource.ConcurrencyStamp, actor, Now.UtcDateTime);
        var second = resource.MaterialVersionId;
        await Assert.That(second).IsNotEqualTo(first);
        resource.SetExternalDestination("protected-envelope", 1, "https://material.example",
            resource.ConcurrencyStamp, actor, Now.UtcDateTime);
        await Assert.That(resource.MaterialVersionId).IsEqualTo(second);
        resource.SetExternalDestination("replacement-protected-envelope", 1, "https://material.example",
            resource.ConcurrencyStamp, actor, Now.UtcDateTime);
        await Assert.That(resource.MaterialVersionId).IsNotEqualTo(second);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task QuarantinePreservesPublicationButBlocksPayloadAndReplacement(bool external)
    {
        var (resource, parent, actor) = EventResourceTestData.CreatePublished();
        if (external)
            resource.SetExternalDestination("protected-envelope", 1, "https://material.example",
                resource.ConcurrencyStamp, actor, Now.UtcDateTime);
        var material = resource.MaterialVersionId!.Value;
        var receipt = Apply(resource);
        await Assert.That(receipt.TenantId).IsEqualTo(resource.TenantId);
        await Assert.That(receipt.EventId).IsEqualTo(resource.EventId);
        await Assert.That(receipt.EventResourceId).IsEqualTo(resource.Id);
        await Assert.That(receipt.MaterialVersionId).IsEqualTo(material);
        await Assert.That(() => ((ITenantEntity)receipt).TenantId = Guid.CreateVersion7())
            .Throws<InvalidOperationException>();
        await Assert.That(resource.PublicationStateId).IsEqualTo((int)EventResourcePublicationStateEnum.Published);
        await Assert.That(resource.HasModerationQuarantine).IsTrue();
        await Assert.That(resource.HasPublishablePayload()).IsFalse();
        await Assert.That(EventResourceTestData.Decision(resource, parent, actor).CanAccess).IsFalse();
        await Assert.That(() => resource.SetStoredFile(Guid.CreateVersion7(),
            resource.ConcurrencyStamp, actor, Now.UtcDateTime)).Throws<InvalidOperationException>();
        await Assert.That(() => resource.SetExternalDestination("different-envelope", 1, "https://other.example",
            resource.ConcurrencyStamp, actor, Now.UtcDateTime)).Throws<InvalidOperationException>();
        await Assert.That(resource.MaterialVersionId).IsEqualTo(material);
        resource.Withdraw(resource.ConcurrencyStamp, actor, Now.UtcDateTime);
        await Assert.That(() => resource.Republish(parent, true, resource.ConcurrencyStamp, actor, Now.UtcDateTime))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task DraftQuarantineCannotBePublishedByAnOrdinarySafetyAssertion()
    {
        var (resource, parent, actor) = EventResourceTestData.CreateDraft();
        resource.SetStoredFile(Guid.CreateVersion7(), resource.ConcurrencyStamp, actor, Now.UtcDateTime);
        Apply(resource);
        await Assert.That(() => resource.Publish(parent, true, resource.ConcurrencyStamp, actor, Now.UtcDateTime))
            .Throws<InvalidOperationException>();
        await Assert.That(resource.PublicationStateId).IsEqualTo((int)EventResourcePublicationStateEnum.Draft);
    }

    [Test]
    public async Task EachEffectOrdinalRetainsItsOwnCauseAndExactRetryDoesNotDuplicate()
    {
        var (resource, _, _) = EventResourceTestData.CreatePublished();
        var effect = Guid.CreateVersion7();
        var first = Apply(resource, effect: effect, ordinal: 0);
        var consequence = Apply(resource, effect: effect, ordinal: 1);
        var independent = Apply(resource);
        var retry = resource.ApplyModerationQuarantine(first.Id, effect, 0, first.MaterialVersionId,
            resource.ConcurrencyStamp, Now.AddDays(30));
        await Assert.That(retry).IsSameReferenceAs(first);
        await Assert.That(retry.AppliedAt).IsEqualTo(Now);
        await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(3);
        await Assert.That(resource.ModerationQuarantines.Count).IsEqualTo(3);
        await Assert.That(consequence.Id).IsNotEqualTo(first.Id);
        await Assert.That(independent.EffectId).IsNotEqualTo(effect);
        await Assert.That(resource.HasModerationQuarantine).IsTrue();
    }

    [Test]
    public async Task DuplicateCauseCannotBeReidentifiedOrBorrowAnotherMaterialVersion()
    {
        var (resource, _, _) = EventResourceTestData.CreatePublished();
        var receipt = Apply(resource);
        await Assert.That(() => resource.ApplyModerationQuarantine(Guid.CreateVersion7(),
            receipt.EffectId, receipt.BindingOrdinal, receipt.MaterialVersionId, resource.ConcurrencyStamp, Now))
            .Throws<InvalidOperationException>();
        await Assert.That(() => resource.ApplyModerationQuarantine(receipt.Id,
            receipt.EffectId, receipt.BindingOrdinal, Guid.CreateVersion7(), resource.ConcurrencyStamp, Now))
            .Throws<InvalidOperationException>();
        await Assert.That(() => resource.ApplyModerationQuarantine(receipt.Id,
            Guid.CreateVersion7(), receipt.BindingOrdinal, receipt.MaterialVersionId, resource.ConcurrencyStamp, Now))
            .Throws<InvalidOperationException>();
        await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(1);
    }

    [Test]
    public async Task MetadataPolicyStampAndElapsedPersonalIntervalCannotRemoveOwnedControl()
    {
        var (resource, parent, actor) = EventResourceTestData.CreatePublished();
        var receipt = Apply(resource);
        resource.UpdateMetadata(EventResourceTestData.Metadata with { Title = "Updated" },
            resource.ConcurrencyStamp, actor, Now.AddYears(1).UtcDateTime);
        resource.ReplacePolicy(resource.Availability, resource.AudienceRules,
            resource.ConcurrencyStamp, actor, Now.AddYears(1).UtcDateTime);
        resource.ConcurrencyStamp = Guid.NewGuid();
        await Assert.That(resource.MaterialVersionId).IsEqualTo(receipt.MaterialVersionId);
        await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(1);
        await Assert.That(resource.HasPublishablePayload()).IsFalse();
        await Assert.That(EventResourceTestData.Decision(resource, parent, actor).CanAccess).IsFalse();
        await Assert.That(() => resource.SetStoredFile(Guid.CreateVersion7(),
            resource.ConcurrencyStamp, actor, Now.AddYears(1).UtcDateTime)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task DeletionAndParentRedactionPreserveControlWithoutRestoringMaterial()
    {
        var (resource, _, actor) = EventResourceTestData.CreatePublished();
        var receipt = Apply(resource);
        resource.Delete(resource.ConcurrencyStamp, actor, Now.UtcDateTime);
        await Assert.That(resource.MaterialVersionId).IsEqualTo(receipt.MaterialVersionId);
        await Assert.That(resource.ModerationQuarantines.Single()).IsSameReferenceAs(receipt);
        await Assert.That(resource.HasModerationQuarantine).IsTrue();
        await Assert.That(resource.StorageObjectId).IsNull();
        await Assert.That(() => ((ISoftDeletable)resource).IsDeleted = false).Throws<InvalidOperationException>();
        await Assert.That(() => resource.SetStoredFile(Guid.CreateVersion7(),
            resource.ConcurrencyStamp, actor, Now.UtcDateTime)).Throws<InvalidOperationException>();
        resource.ApplyParentModeration("Unavailable", null, Now.UtcDateTime);
        await Assert.That(resource.HasModerationQuarantine).IsTrue();
        await Assert.That(resource.HasPublishablePayload()).IsFalse();
    }

    [Test]
    public async Task StaleOrPayloadlessApplicationDoesNotCreateAControl()
    {
        var (resource, _, actor) = EventResourceTestData.CreateDraft();
        await Assert.That(() => resource.ApplyModerationQuarantine(Guid.CreateVersion7(),
            Guid.CreateVersion7(), 0, Guid.CreateVersion7(), resource.ConcurrencyStamp, Now))
            .Throws<InvalidOperationException>();
        resource.SetStoredFile(Guid.CreateVersion7(), resource.ConcurrencyStamp, actor, Now.UtcDateTime);
        var oldMaterial = resource.MaterialVersionId!.Value;
        resource.SetStoredFile(Guid.CreateVersion7(), resource.ConcurrencyStamp, actor, Now.UtcDateTime);
        await Assert.That(() => resource.ApplyModerationQuarantine(Guid.CreateVersion7(),
            Guid.CreateVersion7(), 0, oldMaterial, resource.ConcurrencyStamp, Now)).Throws<InvalidOperationException>();
        await Assert.That(() => resource.ApplyModerationQuarantine(Guid.CreateVersion7(),
            Guid.CreateVersion7(), 0, resource.MaterialVersionId!.Value, Guid.NewGuid(), Now)).Throws<InvalidOperationException>();
        await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(0);
        await Assert.That(resource.ModerationQuarantines.Count).IsEqualTo(0);
        await Assert.That(resource.HasPublishablePayload()).IsTrue();
    }

    [Test]
    [Arguments(-1)]
    [Arguments(64)]
    public async Task InvalidOrdinalCannotPartiallyLatchTheResource(int ordinal)
    {
        var (resource, _, _) = EventResourceTestData.CreatePublished();
        await Assert.That(() => Apply(resource, ordinal: ordinal)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(0);
        await Assert.That(resource.HasModerationQuarantine).IsFalse();
    }

    private static EventResourceModerationQuarantine Apply(
        EventResource resource, Guid? effect = null, int ordinal = 0) =>
        resource.ApplyModerationQuarantine(Guid.CreateVersion7(), effect ?? Guid.CreateVersion7(), ordinal,
            resource.MaterialVersionId!.Value, resource.ConcurrencyStamp, Now);
}
