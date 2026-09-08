using Explore.Domain;
using Explore.Domain.Interfaces;

namespace Event.Domain.UnitTests.Entities;

public sealed class AdmissionScannerCapabilityInvariantTests
{
    private static readonly DateTime IssuedAt = new(2026, 8, 26, 8, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task CreateCapturesTenantEventDigestActionsTargetsAndIssuanceAuditWithoutPlaintext()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid targetId = Guid.CreateVersion7();
        Guid issuedBy = Guid.CreateVersion7();
        AdmissionScannerCapability capability = AdmissionScannerCapability.Issue(
            Guid.CreateVersion7(),
            tenantId,
            Guid.CreateVersion7(),
            eventId,
            targetId,
            7,
            new string('d', 64),
            "North entrance scanner",
            AdmissionScannerCapabilityAction.CheckIn | AdmissionScannerCapabilityAction.Undo,
            IssuedAt.AddHours(8),
            issuedBy,
            IssuedAt);

        await Assert.That(capability).IsAssignableTo<ITenantEntity>();
        await Assert.That(capability).IsAssignableTo<IConcurrencyAware>();
        await Assert.That(capability.TenantId).IsEqualTo(tenantId);
        await Assert.That(capability.EventId).IsEqualTo(eventId);
        await Assert.That(capability.LookupKeyVersion).IsEqualTo(7);
        await Assert.That(capability.DeviceLabel).IsEqualTo("North entrance scanner");
        await Assert.That(capability.Actions).IsEqualTo(
            AdmissionScannerCapabilityAction.CheckIn | AdmissionScannerCapabilityAction.Undo);
        await Assert.That(capability.AdmissionTargetId).IsEqualTo(targetId);
        await Assert.That(typeof(AdmissionScannerCapability).GetProperty("Targets")).IsNull();
        await Assert.That(typeof(AdmissionScannerCapability).Assembly.GetType(
            "Explore.Domain.AdmissionScannerCapabilityTarget")).IsNull();
        await Assert.That(capability.IssuedByActorId).IsEqualTo(issuedBy);
        await Assert.That(capability.IssuedAt).IsEqualTo(IssuedAt);
        await Assert.That(capability.ExpiresAt).IsEqualTo(IssuedAt.AddHours(8));
        await Assert.That(capability.ConcurrencyStamp.Version).IsEqualTo(7);
        await Assert.That(capability.ToString()).DoesNotContain(capability.LookupDigest);

        string[] properties = typeof(AdmissionScannerCapability).GetProperties()
            .Select(property => property.Name)
            .ToArray();
        await Assert.That(properties.Any(name =>
            name.Contains("Plaintext", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Bearer", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RawToken", StringComparison.OrdinalIgnoreCase))).IsFalse();
    }

    [Test]
    public async Task ScopeAndExpiryDecisionsAreExactAndFailClosed()
    {
        Guid targetId = Guid.CreateVersion7();
        AdmissionScannerCapability capability = ValidCapability(
            AdmissionScannerCapabilityAction.CheckIn,
            targetId);

        await Assert.That(capability.Permits(targetId, AdmissionScannerCapabilityAction.CheckIn, IssuedAt))
            .IsTrue();
        await Assert.That(capability.Permits(Guid.CreateVersion7(), AdmissionScannerCapabilityAction.CheckIn, IssuedAt))
            .IsFalse();
        await Assert.That(capability.Permits(targetId, AdmissionScannerCapabilityAction.Undo, IssuedAt))
            .IsFalse();
        await Assert.That(capability.Permits(targetId, AdmissionScannerCapabilityAction.CheckIn, capability.ExpiresAt))
            .IsFalse();
    }

    [Test]
    public async Task RevokeIsImmediateIdempotentAndRetainsBoundedAudit()
    {
        Guid targetId = Guid.CreateVersion7();
        Guid revokedBy = Guid.CreateVersion7();
        AdmissionScannerCapability capability = ValidCapability(
            AdmissionScannerCapabilityAction.CheckIn | AdmissionScannerCapabilityAction.Undo,
            targetId);
        DateTime revokedAt = IssuedAt.AddMinutes(10);

        AdmissionScannerCapabilityRevocationTransition first = capability.Revoke(
            revokedBy, "  Device lost  ", revokedAt);
        AdmissionScannerCapabilityRevocationTransition duplicate = capability.Revoke(
            revokedBy, "Device lost", revokedAt.AddMinutes(1));

        await Assert.That(first).IsEqualTo(AdmissionScannerCapabilityRevocationTransition.Revoked);
        await Assert.That(duplicate).IsEqualTo(AdmissionScannerCapabilityRevocationTransition.AlreadyRevoked);
        await Assert.That(capability.RevokedByActorId).IsEqualTo(revokedBy);
        await Assert.That(capability.RevokedAt).IsEqualTo(revokedAt);
        await Assert.That(capability.RevocationReason).IsEqualTo("Device lost");
        await Assert.That(capability.Permits(targetId, AdmissionScannerCapabilityAction.CheckIn, revokedAt))
            .IsFalse();
    }

    [Test]
    public async Task IssueRejectsUnknownActionsEmptyTargetAndUnboundedLabels()
    {
        Guid targetId = Guid.CreateVersion7();
        await Assert.That(() => ValidCapability((AdmissionScannerCapabilityAction)8, targetId))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => ValidCapability(AdmissionScannerCapabilityAction.CheckIn, Guid.Empty))
            .Throws<ArgumentException>();
        await Assert.That(() => AdmissionScannerCapability.Issue(
                Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                targetId, 1, "digest", new string('d', 129), AdmissionScannerCapabilityAction.CheckIn,
                IssuedAt.AddHours(1), Guid.CreateVersion7(), IssuedAt))
            .Throws<ArgumentException>();
    }

    private static AdmissionScannerCapability ValidCapability(
        AdmissionScannerCapabilityAction actions,
        Guid targetId) => AdmissionScannerCapability.Issue(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        targetId,
        7,
        new string('d', 64),
        "Door scanner",
        actions,
        IssuedAt.AddHours(1),
        Guid.CreateVersion7(),
        IssuedAt);
}
