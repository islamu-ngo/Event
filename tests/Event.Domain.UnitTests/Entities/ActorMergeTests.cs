namespace Event.Domain.UnitTests.Entities;

using Explore.Domain.Enums;

public sealed class ActorMergeTests
{
    [Test]
    public async Task Create_WithVerifiedEvidence_PreservesImmutableMergeFacts()
    {
        var sourceActorId = Guid.CreateVersion7();
        var canonicalActorId = Guid.CreateVersion7();
        var mergedAt = new DateTime(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);
        var mergedBy = Guid.CreateVersion7();

        var merge = ActorMerge.Create(
            sourceActorId,
            canonicalActorId,
            ActorMergeProofKind.VerifiedDid,
            "  did-proof:sha256:abc123  ",
            mergedAt,
            mergedBy);

        await Assert.That(merge.Id).IsNotEqualTo(Guid.Empty);
        await Assert.That(merge.SourceActorId).IsEqualTo(sourceActorId);
        await Assert.That(merge.CanonicalActorId).IsEqualTo(canonicalActorId);
        await Assert.That(merge.ProofKind).IsEqualTo(ActorMergeProofKind.VerifiedDid);
        await Assert.That(merge.EvidenceReference).IsEqualTo("did-proof:sha256:abc123");
        await Assert.That(merge.MergedAt).IsEqualTo(mergedAt);
        await Assert.That(merge.MergedBy).IsEqualTo(mergedBy);
    }

    [Test]
    public async Task Create_WithSameSourceAndCanonicalActor_IsRejected()
    {
        var actorId = Guid.CreateVersion7();

        await Assert.ThrowsAsync<ArgumentException>(() => Task.Run(() => ActorMerge.Create(
            actorId,
            actorId,
            ActorMergeProofKind.VerifiedDid,
            "did-proof:sha256:abc123",
            DomainTestClock.UtcNow,
            Guid.CreateVersion7())));
    }

    [Test]
    public async Task Create_WithoutEvidenceReference_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Task.Run(() => ActorMerge.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            ActorMergeProofKind.VerifiedDid,
            "   ",
            DomainTestClock.UtcNow,
            Guid.CreateVersion7())));
    }
}
