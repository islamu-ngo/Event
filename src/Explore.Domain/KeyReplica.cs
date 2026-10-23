using Explore.Domain.Enums;

namespace Explore.Domain;

/// <summary>
/// One independently byte-verified acknowledgement from a real native runtime owner.
/// The runtime identity is not a host identifier and expires when its exclusive owner exits.
/// </summary>
public sealed class KeyReplica
{
    private KeyReplica() { }
    public Guid Id { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid RuntimeReplicaId { get; private set; }
    public long Sequence { get; private set; }
    public long RingGeneration { get; private set; }
    public string RingCommitment { get; private set; } = string.Empty;
    public string BindingRevision { get; private set; } = string.Empty;
    public SecretSourceType Source { get; private set; }
    public DateTimeOffset VerifiedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }

    public static KeyReplica Acknowledge(Guid id, Guid runtimeReplicaId, ModerationRecognitionKeyLifecycle lifecycle,
        string bindingRevision, SecretSourceType source, DateTimeOffset now, DateTimeOffset validUntil)
    {
        lifecycle.ValidatePersistedState();
        if (now < lifecycle.ChangedAt) throw ModerationRecognitionKeyLifecycle.Unavailable();
        var acknowledgement = new KeyReplica
        {
            Id = id,
            TrustDomainId = lifecycle.Id,
            RuntimeReplicaId = runtimeReplicaId,
            Sequence = checked(lifecycle.AcknowledgementSequence + 1),
            RingGeneration = lifecycle.RingGeneration,
            RingCommitment = lifecycle.Commitment,
            BindingRevision = bindingRevision,
            Source = source,
            VerifiedAt = now,
            ValidUntil = validUntil
        };
        acknowledgement.ValidatePersistedState();
        acknowledgement.Sequence = lifecycle.AllocateAcknowledgement();
        return acknowledgement;
    }

    public bool Matches(ModerationRecognitionKeyLifecycle lifecycle, Guid runtimeReplicaId, DateTimeOffset now)
    {
        ValidatePersistedState();
        return TrustDomainId == lifecycle.Id && RuntimeReplicaId == runtimeReplicaId
            && Sequence == lifecycle.AcknowledgementSequence
            && RingGeneration == lifecycle.RingGeneration && RingCommitment == lifecycle.Commitment
            && now >= VerifiedAt && now < ValidUntil;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, TrustDomainId, RuntimeReplicaId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (RingGeneration is < 1 or long.MaxValue || Sequence is < 1 or long.MaxValue
            || !ModerationDeploymentSource.Digest(RingCommitment)
            || string.IsNullOrWhiteSpace(BindingRevision) || BindingRevision.Length > 256
            || Source is not (SecretSourceType.EnvironmentVariable or SecretSourceType.Infisical)
            || !ModerationRecognitionKeyLifecycle.Finite(VerifiedAt)
            || !ModerationRecognitionKeyLifecycle.Finite(ValidUntil) || ValidUntil <= VerifiedAt)
            throw ModerationRecognitionKeyLifecycle.Unavailable();
    }
    public override string ToString() => nameof(KeyReplica);
}
