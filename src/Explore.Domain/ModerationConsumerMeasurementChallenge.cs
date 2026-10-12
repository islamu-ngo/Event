namespace Explore.Domain;

/// <summary>
/// Immutable original-generation measurement request. A nonce or prepared challenge is not
/// a test result, conformance receipt, permission or promise to activate a publication.
/// </summary>
public sealed class ModerationConsumerMeasurementChallenge
{
    private ModerationConsumerMeasurementChallenge() { }
    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid InstallationId { get; private set; }
    public long InstallationRevision { get; private set; }
    public long InstallationWriterGeneration { get; private set; }
    public string ConsumerId { get; private set; } = string.Empty;
    public int ModeCode { get; private set; }
    public string RuntimeGeneration { get; private set; } = string.Empty;
    public string InventoryFingerprint { get; private set; } = string.Empty;
    public Guid PublicationId { get; private set; }
    public Guid DefinitionId { get; private set; }
    public string BindingDigest { get; private set; } = string.Empty;
    public string ConsumerContractDigest { get; private set; } = string.Empty;
    public string Nonce { get; private set; } = string.Empty;
    public Guid PreparedByHumanId { get; private set; }
    public long WriterGeneration { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset PreparedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }

    public static ModerationConsumerMeasurementChallenge Prepare(
        Guid id, ModerationConsumerInstallation installed, Guid publicationId, Guid definitionId, string bindingDigest,
        string consumerContractDigest, string nonce, Guid humanId, long writerGeneration,
        DateTimeOffset now, DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(installed);
        installed.ValidatePersistedState();
        if (installed.WithdrawnAt is not null || now < installed.InstalledAt
            || now >= installed.ValidUntil || validUntil > installed.ValidUntil
            || writerGeneration <= installed.WriterGeneration)
            throw new ArgumentException("Measurement preparation requires the exact live installation and its finite authority window.");
        var challenge = new ModerationConsumerMeasurementChallenge
        {
            Id = id, HostBindingId = installed.HostBindingId, TrustDomainId = installed.TrustDomainId,
            TenantId = installed.TenantId, InstallationId = installed.Id, InstallationRevision = installed.Revision,
            InstallationWriterGeneration = installed.WriterGeneration, ConsumerId = installed.ConsumerId,
            ModeCode = installed.ModeCode, RuntimeGeneration = installed.RuntimeGeneration,
            InventoryFingerprint = installed.InventoryFingerprint, PublicationId = publicationId,
            DefinitionId = definitionId,
            BindingDigest = bindingDigest, ConsumerContractDigest = consumerContractDigest, Nonce = nonce,
            PreparedByHumanId = humanId, WriterGeneration = writerGeneration, Revision = 1,
            PreparedAt = now.ToUniversalTime(), ValidUntil = validUntil
        };
        challenge.ValidatePersistedState();
        return challenge;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId, InstallationId, PublicationId, DefinitionId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (InstallationRevision != 1 || InstallationWriterGeneration < 1
            || WriterGeneration <= InstallationWriterGeneration || WriterGeneration == long.MaxValue || Revision != 1
            || PreparedByHumanId == Guid.Empty || !ModerationConsumerInstallation.IsConsumerId(ConsumerId) || ModeCode != 1
            || !Digest(RuntimeGeneration) || !Digest(InventoryFingerprint) || !Digest(BindingDigest)
            || !Digest(ConsumerContractDigest) || !Digest(Nonce))
            throw new ArgumentException("Measurement preparation requires immutable original source, binding, contract and nonce coordinates.");
        ModerationQualificationPolicySource.RequireValidity(PreparedAt, ValidUntil);
    }

    private static bool Digest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    public override string ToString() => nameof(ModerationConsumerMeasurementChallenge);
}
