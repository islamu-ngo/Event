using Explore.Domain;

namespace Event.Domain.UnitTests.Entities;

public sealed class WebhookRetentionTests
{
    [Test]
    public async Task Hold_NormalizesReasonAndTracksActiveReleaseLifecycle()
    {
        var placedAt = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);
        var hold = WebhookRetentionHold.Create(
            Guid.CreateVersion7(),
            WebhookRetentionSubjectKind.IncomingMessage,
            Guid.CreateVersion7(),
            " Legal_Investigation ",
            placedAt,
            placedAt.AddDays(30));

        await Assert.That(hold.Id.Version).IsEqualTo(7);
        await Assert.That(hold.SubjectKind).IsEqualTo(WebhookRetentionSubjectKind.IncomingMessage);
        await Assert.That(hold.ReasonCode).IsEqualTo("legal_investigation");
        await Assert.That(hold.IsActiveAt(placedAt.AddDays(1))).IsTrue();
        await Assert.That(hold.IsActiveAt(placedAt.AddDays(30))).IsFalse();

        hold.Release(placedAt.AddDays(2));
        hold.Release(placedAt.AddDays(3));

        await Assert.That(hold.ReleasedAt).IsEqualTo(placedAt.AddDays(2));
        await Assert.That(hold.IsActiveAt(placedAt.AddDays(2))).IsFalse();
    }

    [Test]
    public async Task Hold_RejectsInvalidReasonExpiryAndTimestampKind()
    {
        var placedAt = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);

        await Assert.ThrowsAsync<ArgumentException>(() => Task.FromResult(WebhookRetentionHold.Create(
            Guid.CreateVersion7(),
            WebhookRetentionSubjectKind.OutgoingMessage,
            Guid.CreateVersion7(),
            "not a normalized reason",
            placedAt)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(WebhookRetentionHold.Create(
            Guid.CreateVersion7(),
            WebhookRetentionSubjectKind.OutgoingMessage,
            Guid.CreateVersion7(),
            "legal_hold",
            placedAt,
            placedAt)));
        await Assert.ThrowsAsync<ArgumentException>(() => Task.FromResult(WebhookRetentionHold.Create(
            Guid.CreateVersion7(),
            WebhookRetentionSubjectKind.OutgoingMessage,
            Guid.CreateVersion7(),
            "legal_hold",
            DateTime.SpecifyKind(placedAt, DateTimeKind.Unspecified))));
    }

    [Test]
    public async Task DeliveryPlan_RejectsDeadLetterHorizonShorterThanAttemptHorizon()
    {
        var materializedAt = DomainTestClock.UtcNowOffset;

        await Assert.ThrowsAsync<ArgumentException>(() => Task.FromResult(WebhookDeliveryPlanSnapshot.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            WebhookProviderMode.Local,
            "consumer-v1",
            "contract-v1",
            "standard",
            "webhook-retention-v1",
            materializedAt.AddDays(14),
            materializedAt.AddDays(30),
            materializedAt.AddDays(29),
            materializedAt.AddDays(90),
            materializedAt.AddDays(30),
            materializedAt)));
    }
}
