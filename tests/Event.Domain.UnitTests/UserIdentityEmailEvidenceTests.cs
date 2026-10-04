namespace Event.Domain.UnitTests;

public sealed class UserIdentityEmailEvidenceTests
{
    private static readonly DateTime ObservedAt = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task IndependentBindingsMayProveTheSameCanonicalClaim()
    {
        Guid claimId = Guid.CreateVersion7();
        Guid ownerId = Guid.CreateVersion7();
        Guid firstBinding = Guid.CreateVersion7();
        Guid secondBinding = Guid.CreateVersion7();
        UserIdentityEmailEvidence first = UserIdentityEmailEvidence.Create(ownerId, claimId, firstBinding, ObservedAt);
        UserIdentityEmailEvidence second = UserIdentityEmailEvidence.Create(ownerId, claimId, secondBinding, ObservedAt);

        first.Invalidate();

        await Assert.That(first.IsActive).IsFalse();
        await Assert.That(second.IsActive).IsTrue();
        await Assert.That(second.ClaimId).IsEqualTo(claimId);
        await Assert.That(second.ExternalLoginId).IsEqualTo(secondBinding);
    }

    [Test]
    public async Task AnInvalidatedProofCannotBeReactivatedByChangingContactText()
    {
        UserIdentityEmailEvidence evidence = UserIdentityEmailEvidence.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), ObservedAt);

        evidence.Invalidate();
        evidence.Invalidate();

        await Assert.That(evidence.IsActive).IsFalse();
    }

    [Test]
    public async Task ProofRequiresCanonicalClaimAndExactBinding()
    {
        await Assert.That(() => UserIdentityEmailEvidence.Create(Guid.CreateVersion7(), Guid.Empty, Guid.CreateVersion7(), ObservedAt))
            .Throws<ArgumentException>();
        await Assert.That(() => UserIdentityEmailEvidence.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.Empty, ObservedAt))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ProofRequiresAnExplicitUtcObservation()
    {
        await Assert.That(() => UserIdentityEmailEvidence.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), default))
            .Throws<ArgumentException>();
        await Assert.That(() => UserIdentityEmailEvidence.Create(
                Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), DateTime.SpecifyKind(ObservedAt, DateTimeKind.Unspecified)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ProofRequiresAnAccountOwner()
    {
        await Assert.That(() => UserIdentityEmailEvidence.Create(
                Guid.Empty, Guid.CreateVersion7(), Guid.CreateVersion7(), ObservedAt))
            .Throws<ArgumentException>();
    }
}
