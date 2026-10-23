namespace Explore.Domain;

/// <summary>
/// Protected custody for one original material fact. Only ciphertext is retained. The immutable
/// deadline starts at capture and is capped by the independently adopted policy, never renewed
/// by retries, a new receipt, a finding, restriction or later policy generation.
/// The caller supplies already-protected bytes; storage shape does not authenticate encryption
/// or establish current native permission.
/// </summary>
public sealed class ModerationCaseNarrative
{
    private ModerationCaseNarrative() { }
    private byte[] protectedMaterial = [];
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid CaseId { get; private set; }
    public Guid ReporterUserId { get; private set; }
    public Guid ReceiptId { get; private set; }
    public Guid SourceId { get; private set; }
    public Guid SourceVersion { get; private set; }
    public Guid MaterialFactId { get; private set; }
    public Guid PolicySourceId { get; private set; }
    public Guid AdoptionId { get; private set; }
    public long PolicyWriterGeneration { get; private set; }
    public long AdoptionWriterGeneration { get; private set; }
    public Guid QualificationEpochId { get; private set; }
    public long QualificationRevision { get; private set; }
    public DateTimeOffset CapturedAt { get; private set; }
    public DateTimeOffset PurgeAt { get; private set; }
    public ReadOnlyMemory<byte> ProtectedMaterial => protectedMaterial.ToArray();

    public static ModerationCaseNarrative Capture(Guid id, Guid caseId, ModerationCaseTargetReceipt receipt,
        ModerationCaseNarrativePolicySource policy, ModerationCaseNarrativeAdoption adoption,
        DateTimeOffset now, DateTimeOffset purgeAt, ReadOnlySpan<byte> ciphertext)
    {
        receipt.ValidatePersistedState();
        policy.ValidatePersistedState();
        adoption.ValidatePersistedState();
        if (receipt.RelationshipKind == ModerationCaseRelationshipKind.CaseReadGrant
            || policy.Purpose != ModerationCaseNarrativePurpose.ConfidentialAllegation
            || !adoption.MatchesCurrent(policy, now) || receipt.TargetKind != policy.TargetKind
            || receipt.TenantId != policy.TenantId || receipt.HostBindingId != policy.HostBindingId
            || receipt.TargetOriginTrustDomainId != policy.TrustDomainId || ciphertext.IsEmpty
            || ciphertext.Length > 131072 || now < receipt.IssuedAt || now >= receipt.ExpiresAt
            || purgeAt <= now || purgeAt > adoption.ValidUntil || purgeAt - now > policy.Terms.MaximumRetention)
            throw new ArgumentException("Narrative capture requires exact current material and independent finite custody authority.");
        var row = new ModerationCaseNarrative
        {
            Id = id,
            TenantId = receipt.TenantId,
            TrustDomainId = policy.TrustDomainId,
            HostBindingId = receipt.HostBindingId,
            CaseId = caseId,
            ReporterUserId = receipt.CallerUserId,
            ReceiptId = receipt.Id,
            SourceId = receipt.SourceId,
            SourceVersion = receipt.SourceVersion,
            MaterialFactId = receipt.MaterialFactId,
            PolicySourceId = policy.Id,
            AdoptionId = adoption.Id,
            PolicyWriterGeneration = policy.WriterGeneration,
            AdoptionWriterGeneration = adoption.WriterGeneration,
            QualificationEpochId = receipt.QualificationEpochId,
            QualificationRevision = receipt.QualificationRevision,
            CapturedAt = now,
            PurgeAt = purgeAt,
            protectedMaterial = ciphertext.ToArray()
        };
        row.ValidatePersistedState();
        return row;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, TenantId, TrustDomainId, HostBindingId, CaseId, ReceiptId, SourceId,
            MaterialFactId, PolicySourceId, AdoptionId, QualificationEpochId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        ModerationQualificationPolicySource.RequireValidity(CapturedAt, PurgeAt);
        if (ReporterUserId == Guid.Empty || SourceVersion == Guid.Empty || PolicyWriterGeneration < 1
            || AdoptionWriterGeneration <= PolicyWriterGeneration || QualificationRevision < AdoptionWriterGeneration
            || protectedMaterial.Length is < 1 or > 131072)
            throw new ArgumentException("Narrative custody requires original provenance, protected material and a finite physical deadline.");
    }

    public override string ToString() => nameof(ModerationCaseNarrative);
}
