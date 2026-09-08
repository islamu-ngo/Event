using Explore.Domain;

namespace Event.Domain.UnitTests.Entities;

public sealed class NotificationFanoutOccurrenceTests
{
    [Test]
    public async Task Create_PreservesImmutableBusinessSnapshot()
    {
        Guid id = Guid.CreateVersion7();
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid aggregateVersion = Guid.CreateVersion7();
        DateTime occurredAt = DomainTestClock.UtcNow;

        var occurrence = NotificationFanoutOccurrence.Create(
            id,
            tenantId,
            eventId,
            null,
            occurredAt,
            occurredAt,
            aggregateVersion,
            "{\"fields\":[\"startTime\"]}",
            "{\"startTime\":\"2026-07-18T08:00:00Z\"}",
            "{\"startTime\":\"2026-07-18T09:00:00Z\"}",
            "event.session.updated",
            3,
            2,
            1,
            30,
            occurredAt.AddMinutes(5),
            "event-session",
            eventId,
            "session:update:42",
            occurredAt.AddMinutes(5),
            emailDeliveryPolicyRevision: 0);

        await Assert.That(occurrence.Id).IsEqualTo(id);
        await Assert.That(occurrence.AggregateVersion).IsEqualTo(aggregateVersion);
        await Assert.That(occurrence.ChangeSetJson).IsEqualTo("{\"fields\":[\"startTime\"]}");
        await Assert.That(occurrence.SafeBeforeSnapshotJson).Contains("08:00:00Z");
        await Assert.That(occurrence.SafeAfterSnapshotJson).Contains("09:00:00Z");
        await Assert.That(typeof(NotificationFanoutOccurrence).GetProperty(nameof(NotificationFanoutOccurrence.ChangeSetJson))!.SetMethod!.IsPublic).IsFalse();
        await Assert.That(typeof(NotificationFanoutOccurrence).GetProperty(nameof(NotificationFanoutOccurrence.SafeBeforeSnapshotJson))!.SetMethod!.IsPublic).IsFalse();
        await Assert.That(typeof(NotificationFanoutOccurrence).GetProperty(nameof(NotificationFanoutOccurrence.SafeAfterSnapshotJson))!.SetMethod!.IsPublic).IsFalse();
    }

    [Test]
    public async Task Supersede_ChangesOnlyExplicitLifecycleMetadata()
    {
        DateTime now = DomainTestClock.UtcNow;
        var occurrence = NotificationFanoutOccurrence.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null,
            now, now, Guid.CreateVersion7(), "{}", "{}", "{}",
            "event.cancelled", 1, 2, 1, 100, now,
            "event", Guid.CreateVersion7(), "event:cancelled", null,
            emailDeliveryPolicyRevision: 0);
        string before = occurrence.SafeBeforeSnapshotJson;
        Guid replacementId = Guid.CreateVersion7();

        occurrence.Supersede(replacementId, "newer_occurrence", now.AddSeconds(1));

        await Assert.That(occurrence.State).IsEqualTo(NotificationFanoutOccurrenceState.Superseded);
        await Assert.That(occurrence.SupersededByOccurrenceId).IsEqualTo(replacementId);
        await Assert.That(occurrence.SuppressionReason).IsEqualTo("newer_occurrence");
        await Assert.That(occurrence.SafeBeforeSnapshotJson).IsEqualTo(before);
    }
}
