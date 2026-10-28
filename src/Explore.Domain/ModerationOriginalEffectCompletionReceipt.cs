namespace Explore.Domain;

/// <summary>
/// Immutable receipt of the native owner's complete original proof and exact lease transition.
/// It is deleted with the original source, supplies no disclosure grant and cannot authorize
/// late completion after original custody ends. Recording it does not mutate the effect.
/// </summary>
public sealed class ModerationOriginalEffectCompletionReceipt
{
    private ModerationOriginalEffectCompletionReceipt() { }

    public Guid Id { get; private set; }
    public Guid EffectId { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid LeaseId { get; private set; }
    public long LeaseRevision { get; private set; }
    public DateTimeOffset LeaseStartedAt { get; private set; }
    public DateTimeOffset LeaseExpiresAt { get; private set; }
    public Guid PublicationId { get; private set; }
    public Guid ProfileId { get; private set; }
    public string SnapshotHash { get; private set; } = string.Empty;
    public string EffectBindingHash { get; private set; } = string.Empty;
    public string SelectionDigest { get; private set; } = string.Empty;
    public string ProofDigest { get; private set; } = string.Empty;
    public int OriginalCauseCount { get; private set; }
    public DateTimeOffset CompletedAt { get; private set; }
    public DateTimeOffset QualifiedUntil { get; private set; }
    public DateTimeOffset RetainUntil { get; private set; }

    public static ModerationOriginalEffectCompletionReceipt Record(
        Guid id, ModerationEffectIntent effect, ModerationNoticeSource source, Guid hostBindingId,
        Guid leaseId, long leaseRevision, Guid publicationId, Guid profileId,
        string selectionDigest, string proofDigest, int originalCauseCount,
        DateTimeOffset completedAt, DateTimeOffset qualifiedUntil)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(source);
        effect.ValidatePersistedState();
        if (!source.Matches(effect) || source.TenantId is not { } tenantId
            || source.RetainUntil is not { } retainUntil || effect.CompletedAt is not null
            || effect.OriginalNoticeCleanupId is not null || effect.LeaseId != leaseId
            || effect.Revision != leaseRevision || effect.LeaseStartedAt is not { } started
            || effect.LeaseExpiresAt is not { } expires)
            throw new ArgumentException("Completion requires the exact pending lease and retained original source.");
        var receipt = new ModerationOriginalEffectCompletionReceipt
        {
            Id = id,
            EffectId = effect.Id,
            HostBindingId = hostBindingId,
            TenantId = tenantId,
            TrustDomainId = effect.TrustDomainId,
            LeaseId = leaseId,
            LeaseRevision = leaseRevision,
            LeaseStartedAt = started,
            LeaseExpiresAt = expires,
            PublicationId = publicationId,
            ProfileId = profileId,
            SnapshotHash = source.SnapshotHash,
            EffectBindingHash = source.EffectBindingHash,
            SelectionDigest = selectionDigest,
            ProofDigest = proofDigest,
            OriginalCauseCount = originalCauseCount,
            CompletedAt = completedAt,
            QualifiedUntil = qualifiedUntil,
            RetainUntil = retainUntil
        };
        receipt.ValidatePersistedState();
        return receipt;
    }

    public bool MatchesCompletion(ModerationEffectIntent effect) =>
        EffectId == effect.Id && TrustDomainId == effect.TrustDomainId
        && effect.LeaseId == LeaseId && effect.Revision == LeaseRevision + 1
        && effect.LeaseStartedAt == LeaseStartedAt && effect.LeaseExpiresAt == LeaseExpiresAt
        && effect.CompletedAt == CompletedAt;

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, EffectId, HostBindingId, TenantId, TrustDomainId, LeaseId, PublicationId, ProfileId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (LeaseRevision is < 1 or long.MaxValue || OriginalCauseCount is < 1 or > 64
            || !Hash(SnapshotHash) || !Hash(EffectBindingHash) || !Hash(SelectionDigest) || !Hash(ProofDigest)
            || !Instant(LeaseStartedAt) || !Instant(LeaseExpiresAt) || !Instant(CompletedAt)
            || !Instant(QualifiedUntil) || !Instant(RetainUntil)
            || CompletedAt < LeaseStartedAt || CompletedAt >= QualifiedUntil
            || QualifiedUntil > LeaseExpiresAt || QualifiedUntil > RetainUntil)
            throw new ArgumentException("Completion cannot outlive its original proof, privacy ceiling or exact lease.");
    }

    private static bool Hash(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigitLower);
    private static bool Instant(DateTimeOffset value) => value.Offset == TimeSpan.Zero
        && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;
    public override string ToString() => nameof(ModerationOriginalEffectCompletionReceipt);
}
