using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Entities;

public sealed class ModerationResourceGenerationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Generation_RetainsNativeGuidStampsRatherThanReinterpretingThemAsRevision(bool stored)
    {
        var generation = Generation(stored);
        generation.ValidatePersistedState();
        await Assert.That(generation.Revision).IsEqualTo(1L);
        await Assert.That(generation.ResourceStamp.Version).IsEqualTo(4);
        await Assert.That(generation.MaterialVersionId.Version).IsEqualTo(7);
        await Assert.That(generation.ToString()).IsEqualTo(nameof(ModerationResourceGeneration));
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public async Task IncompleteOrExhaustedObservation_IsRejected(int substitution)
    {
        var generation = Generation(false, substitution);
        await Assert.That(() => generation.ValidatePersistedState()).Throws<ArgumentException>();
    }

    private static ModerationResourceGeneration Generation(bool stored, int substitution = -1)
    {
        var now = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        return new()
        {
            Id = Guid.CreateVersion7(), HostBindingId = Guid.CreateVersion7(), TrustDomainId = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(), ResourceId = Guid.CreateVersion7(),
            Revision = substitution == 0 ? long.MaxValue : 1, ObservedByHumanId = Guid.CreateVersion7(), WriterGeneration = 2,
            ObservedAt = now, ValidUntil = substitution == 1 ? now : now.AddMinutes(10),
            ResourceStamp = Guid.NewGuid(), EventId = Guid.CreateVersion7(), EventStamp = Guid.NewGuid(),
            MaterialVersionId = substitution == 4 ? Guid.Empty : substitution == 5 ? Guid.NewGuid() : Guid.CreateVersion7(),
            ActorId = Guid.CreateVersion7(), ActorStamp = Guid.NewGuid(),
            OrganizerActorId = substitution == 2 ? Guid.CreateVersion7() : null, OrganizerActorStamp = null,
            DeliveryType = stored || substitution == 3 ? EventResourceDeliveryTypeEnum.StoredFile : EventResourceDeliveryTypeEnum.ExternalLink,
            StorageObjectId = stored ? Guid.CreateVersion7() : null, StorageObjectStamp = stored ? Guid.NewGuid() : null,
            StorageProviderBindingId = stored ? Guid.CreateVersion7() : null, StorageProvider = stored ? StorageProviders.Local : null
        };
    }
}
