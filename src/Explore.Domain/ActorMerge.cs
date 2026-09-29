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
    public Guid TargetActorId { get; private set; }
    public Actor TargetActor { get; private set; } = null!;
    public ActorMergeProofKind ProofKind { get; private set; }
    public string EvidenceReference { get; private set; } = string.Empty;
    public DateTime MergedAt { get; private set; }
    public Guid MergedBy { get; private set; }

    public static ActorMerge Create(
        Guid sourceActorId,
        Guid targetActorId,
        ActorMergeProofKind proofKind,
        string evidenceReference,
        DateTime mergedAt,
        Guid mergedBy)
    {
        if (sourceActorId == targetActorId)
        {
            throw new ArgumentException("Source and target Actor must differ.", nameof(targetActorId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceReference);
        return new ActorMerge
        {
            Id = Guid.CreateVersion7(),
            SourceActorId = sourceActorId,
            TargetActorId = targetActorId,
            ProofKind = proofKind,
            EvidenceReference = evidenceReference.Trim(),
            MergedAt = mergedAt,
            MergedBy = mergedBy
        };
    }
}
