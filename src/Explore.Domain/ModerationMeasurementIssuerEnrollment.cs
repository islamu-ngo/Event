namespace Explore.Domain;

/// <summary>
/// Accountable PlatformAdmin enrollment of a measurement issuer's public key and approved executable digests.
/// Issuer authenticity is not evidence of uncompromised host execution, conformance, or permission to restrict.
/// Original trust metadata and writer generation remain immutable after terminal revocation.
/// </summary>
public sealed class ModerationMeasurementIssuerEnrollment
{
    private ModerationMeasurementIssuerEnrollment() { }

    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid KeyId { get; private set; }
    public string Issuer { get; private set; } = string.Empty;
    public string PublicKeySpkiBase64 { get; private set; } = string.Empty;
    public string RunnerDigest { get; private set; } = string.Empty;
    public string CatalogueDigest { get; private set; } = string.Empty;
    public string TestBundleDigest { get; private set; } = string.Empty;
    public Guid ApprovedByHumanId { get; private set; }
    public long WriterGeneration { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset ApprovedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public Guid? RevokedByHumanId { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>
    /// Records separately approved trust metadata. The accepting host must verify PlatformAdmin authority,
    /// ancestry, and the actual ECDsa P-256 public SPKI before calling this byte-format-only domain operation.
    /// </summary>
    public static ModerationMeasurementIssuerEnrollment Enroll(
        Guid id, Guid hostBindingId, Guid trustDomainId, Guid tenantId, Guid keyId,
        string issuer, string publicKeySpkiBase64, string runnerDigest, string catalogueDigest,
        string testBundleDigest, Guid approvedByHumanId, long writerGeneration,
        DateTimeOffset approvedAt, DateTimeOffset validUntil)
    {
        var source = new ModerationMeasurementIssuerEnrollment
        {
            Id = id,
            HostBindingId = hostBindingId,
            TrustDomainId = trustDomainId,
            TenantId = tenantId,
            KeyId = keyId,
            Issuer = issuer,
            PublicKeySpkiBase64 = publicKeySpkiBase64,
            RunnerDigest = runnerDigest,
            CatalogueDigest = catalogueDigest,
            TestBundleDigest = testBundleDigest,
            ApprovedByHumanId = approvedByHumanId,
            WriterGeneration = writerGeneration,
            Revision = 1,
            ApprovedAt = approvedAt.ToUniversalTime(),
            ValidUntil = validUntil.ToUniversalTime()
        };
        source.ValidatePersistedState();
        return source;
    }

    public void Revoke(long expectedRevision, Guid humanId, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (expectedRevision != Revision || RevokedAt is not null)
            throw new InvalidOperationException("Issuer revocation requires the original live enrollment revision.");
        now = now.ToUniversalTime();
        if (humanId == Guid.Empty || now < ApprovedAt || now == DateTimeOffset.MaxValue)
            throw new ArgumentException("Issuer revocation requires accountable administration and finite non-regressing time.");
        Revision = 2;
        RevokedByHumanId = humanId;
        RevokedAt = now;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId, KeyId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (KeyId == Id || Issuer is not { Length: >= 1 and <= 128 }
            || !Issuer.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')
            || !CanonicalPublicKeyEncoding(PublicKeySpkiBase64)
            || !Digest(RunnerDigest) || !Digest(CatalogueDigest) || !Digest(TestBundleDigest)
            || ApprovedByHumanId == Guid.Empty || WriterGeneration is < 1 or long.MaxValue)
            throw new ArgumentException("Issuer enrollment requires distinct key identity, canonical public metadata and accountable finite generation.");
        ModerationQualificationPolicySource.RequireValidity(ApprovedAt, ValidUntil);
        if (!(Revision == 1 && RevokedAt is null && RevokedByHumanId is null
            || Revision == 2 && RevokedAt is { } revoked && RevokedByHumanId is { } human && human != Guid.Empty
                && revoked >= ApprovedAt && revoked != DateTimeOffset.MaxValue && revoked.Offset == TimeSpan.Zero))
            throw new ArgumentException("Issuer revocation state is inconsistent.");
    }

    private static bool CanonicalPublicKeyEncoding(string? value)
    {
        if (value is not { Length: >= 1 and <= 1024 })
            return false;
        Span<byte> bytes = stackalloc byte[768];
        return Convert.TryFromBase64String(value, bytes, out var written)
            && written > 0 && Convert.ToBase64String(bytes[..written]) == value;
    }

    private static bool Digest(string? value) =>
        value is { Length: 64 } && value.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');

    public override string ToString() => nameof(ModerationMeasurementIssuerEnrollment);
}
