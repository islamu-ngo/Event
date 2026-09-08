using Explore.Domain.Enums;

namespace Explore.Domain;

public class ActorMerge
{
    private ActorMerge()
    {
    }

    public Guid Id { get; private set; }
    public Guid SourceActorId { get; private set; }
    public Actor SourceActor { get; private set; } = null!;
    public Guid CanonicalActorId { get; private set; }
    public Actor CanonicalActor { get; private set; } = null!;
    public ActorMergeProofKind ProofKind { get; private set; }
    public string EvidenceReference { get; private set; } = string.Empty;
    public DateTime MergedAt { get; private set; }
    public Guid MergedBy { get; private set; }

    public static ActorMerge Create(
        Guid sourceActorId,
        Guid canonicalActorId,
        ActorMergeProofKind proofKind,
        string evidenceReference,
        DateTime mergedAt,
        Guid mergedBy)
    {
        if (sourceActorId == canonicalActorId)
        {
            throw new ArgumentException("Source and canonical Actor must differ.", nameof(canonicalActorId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceReference);
        return new ActorMerge
        {
            Id = Guid.CreateVersion7(),
            SourceActorId = sourceActorId,
            CanonicalActorId = canonicalActorId,
            ProofKind = proofKind,
            EvidenceReference = evidenceReference.Trim(),
            MergedAt = mergedAt,
            MergedBy = mergedBy
        };
    }
}
