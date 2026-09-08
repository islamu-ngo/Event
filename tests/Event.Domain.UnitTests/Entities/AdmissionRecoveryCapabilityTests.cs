namespace Event.Domain.UnitTests.Entities;

public sealed class AdmissionRecoveryCapabilityTests
{
    private static readonly DateTime CreatedAt = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
    private const string Digest = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    [Test]
    public async Task ConsumeIsSingleUseAndRotationIsMonotonic()
    {
        AdmissionRecoveryCapability capability = Create();

        AdmissionRecoveryTransitionOutcome first = capability.TryConsume(CreatedAt.AddMinutes(1));
        AdmissionRecoveryTransitionOutcome replay = capability.TryConsume(CreatedAt.AddMinutes(2));
        AdmissionRecoveryTransitionOutcome rotateAfterConsume = capability.TryRotate(CreatedAt.AddMinutes(3));

        await Assert.That(first).IsEqualTo(AdmissionRecoveryTransitionOutcome.Consumed);
        await Assert.That(replay).IsEqualTo(AdmissionRecoveryTransitionOutcome.AlreadyConsumed);
        await Assert.That(rotateAfterConsume).IsEqualTo(AdmissionRecoveryTransitionOutcome.AlreadyConsumed);
        await Assert.That(capability.ConsumedAt).IsEqualTo(CreatedAt.AddMinutes(1));
        await Assert.That(capability.RotatedAt).IsNull();
        await Assert.That(capability.ActiveUniquenessSlot).IsEqualTo(1);
    }

    [Test]
    public async Task RotationMakesOldCapabilityPermanentlyUnavailable()
    {
        AdmissionRecoveryCapability capability = Create();

        AdmissionRecoveryTransitionOutcome rotated = capability.TryRotate(CreatedAt.AddMinutes(1));
        AdmissionRecoveryTransitionOutcome consumed = capability.TryConsume(CreatedAt.AddMinutes(2));

        await Assert.That(rotated).IsEqualTo(AdmissionRecoveryTransitionOutcome.Rotated);
        await Assert.That(consumed).IsEqualTo(AdmissionRecoveryTransitionOutcome.Rotated);
        await Assert.That(capability.RotatedAt).IsEqualTo(CreatedAt.AddMinutes(1));
        await Assert.That(capability.ActiveUniquenessSlot).IsEqualTo(1);
    }

    [Test]
    public async Task ExpiredCapabilityNeverMutatesAndDiagnosticsRedactDigest()
    {
        AdmissionRecoveryCapability capability = Create(expiresAt: CreatedAt.AddMinutes(1));

        AdmissionRecoveryTransitionOutcome outcome = capability.TryConsume(CreatedAt.AddMinutes(1));

        await Assert.That(outcome).IsEqualTo(AdmissionRecoveryTransitionOutcome.Expired);
        await Assert.That(capability.ConsumedAt).IsNull();
        await Assert.That(capability.ToString()).DoesNotContain(Digest);
        await Assert.That(capability.ToString()).Contains("<redacted>");
    }

    [Test]
    public async Task CreationRejectsInvalidLineageAndTime()
    {
        await Assert.That(() => AdmissionRecoveryCapability.Create(
            Guid.NewGuid(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "TicketRecovery",
            1,
            1,
            Digest,
            CreatedAt.AddHours(1),
            CreatedAt)).Throws<ArgumentException>();
        await Assert.That(() => AdmissionRecoveryCapability.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "TicketRecovery",
            1,
            1,
            Digest,
            CreatedAt,
            CreatedAt)).Throws<ArgumentException>();
    }

    private static AdmissionRecoveryCapability Create(DateTime? expiresAt = null) =>
        AdmissionRecoveryCapability.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "TicketRecovery",
            1,
            1,
            Digest,
            expiresAt ?? CreatedAt.AddHours(1),
            CreatedAt);
}
