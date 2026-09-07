using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Entities;

public sealed class RegistrationInventoryHoldTests
{
    [Test]
    public async Task TryExpire_WhenActiveAndDue_ExpiresAndReleasesCapacity()
    {
        var createdAt = new DateTime(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);
        var hold = RegistrationInventoryHold.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            quantity: 1,
            createdAt,
            createdAt.AddMinutes(10));

        var expired = hold.TryExpire(createdAt.AddMinutes(10));

        await Assert.That(expired).IsTrue();
        await Assert.That(hold.RegistrationInventoryHoldStatusId).IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Expired);
        await Assert.That(hold.IsCapacityAllocated).IsFalse();
        await Assert.That(hold.ReleasedAt).IsEqualTo(createdAt.AddMinutes(10));
    }

    [Test]
    public async Task TryConsume_WhenExpired_DoesNotConsumeCapacity()
    {
        var createdAt = new DateTime(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);
        var hold = RegistrationInventoryHold.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            quantity: 1,
            createdAt,
            createdAt.AddMinutes(10));

        var consumed = hold.TryConsume(createdAt.AddMinutes(10));

        await Assert.That(consumed).IsFalse();
        await Assert.That(hold.RegistrationInventoryHoldStatusId).IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Active);
        await Assert.That(hold.IsCapacityAllocated).IsTrue();
    }

    [Test]
    public async Task TryConsume_WhenActiveBeforeExpiry_ConsumesWithoutReleasingCapacity()
    {
        var createdAt = new DateTime(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);
        var hold = RegistrationInventoryHold.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            quantity: 1,
            createdAt,
            createdAt.AddMinutes(10));

        var consumed = hold.TryConsume(createdAt.AddMinutes(9));

        await Assert.That(consumed).IsTrue();
        await Assert.That(hold.RegistrationInventoryHoldStatusId).IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Consumed);
        await Assert.That(hold.IsCapacityAllocated).IsTrue();
        await Assert.That(hold.ReleasedAt).IsNull();
    }

    [Test]
    public async Task TryRelease_WhenReplayed_PreservesOriginalRelease()
    {
        var createdAt = new DateTime(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);
        var hold = RegistrationInventoryHold.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            quantity: 1,
            createdAt,
            createdAt.AddMinutes(10));

        var released = hold.TryRelease(createdAt.AddMinutes(1));
        var replayed = hold.TryRelease(createdAt.AddMinutes(2));

        await Assert.That(released).IsTrue();
        await Assert.That(replayed).IsFalse();
        await Assert.That(hold.RegistrationInventoryHoldStatusId).IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Released);
        await Assert.That(hold.ReleasedAt).IsEqualTo(createdAt.AddMinutes(1));
        await Assert.That(hold.IsCapacityAllocated).IsFalse();
    }
}
