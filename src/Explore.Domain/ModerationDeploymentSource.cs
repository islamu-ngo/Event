namespace Explore.Domain;

/// <summary>
/// Retained native deployment observation for one exact publication/profile/mode.
/// Neither this observation nor its adoption asserts measured conformance or prerequisite readiness.
/// </summary>
public sealed class ModerationDeploymentSource
{
    private ModerationDeploymentSource() { }
    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid PolicyId { get; private set; }
    public long PolicyRevision { get; private set; }
    public Guid PublicationId { get; private set; }
    public Guid DefinitionId { get; private set; }
    public Guid ProfileId { get; private set; }
    public int ModeCode { get; private set; }
    public string ProposedStateDigest { get; private set; } = string.Empty;
    public string InventoryFingerprint { get; private set; } = string.Empty;
    public string DatabaseConfigurationDigest { get; private set; } = string.Empty;
    public string DatabaseProviderCode { get; private set; } = string.Empty;
    public string DatabaseProviderVersion { get; private set; } = string.Empty;
    public List<ModerationDeploymentConsumer> Consumers { get; private set; } = [];
    public Guid RecordedByHumanId { get; private set; }
    public long WriterGeneration { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }

    public static ModerationDeploymentSource Record(
        Guid id, Guid hostId, Guid trustId, Guid tenantId, Guid policyId, long policyRevision,
        Guid publicationId, Guid definitionId, Guid profileId, int modeCode,
        string proposedStateDigest, string inventoryFingerprint, string databaseConfigurationDigest,
        string databaseProviderCode, string databaseProviderVersion, IEnumerable<ModerationDeploymentConsumer> consumers,
        Guid humanId, long writerGeneration, DateTimeOffset now, DateTimeOffset validUntil)
    {
        var source = new ModerationDeploymentSource
        {
            Id = id, HostBindingId = hostId, TrustDomainId = trustId, TenantId = tenantId,
            PolicyId = policyId, PolicyRevision = policyRevision, PublicationId = publicationId,
            DefinitionId = definitionId, ProfileId = profileId, ModeCode = modeCode,
            ProposedStateDigest = proposedStateDigest, InventoryFingerprint = inventoryFingerprint,
            DatabaseConfigurationDigest = databaseConfigurationDigest, DatabaseProviderCode = databaseProviderCode,
            DatabaseProviderVersion = databaseProviderVersion, Consumers = consumers.ToList(),
            RecordedByHumanId = humanId, WriterGeneration = writerGeneration, Revision = 1,
            RecordedAt = now, ValidUntil = validUntil
        };
        source.ValidatePersistedState();
        return source;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId, PolicyId, PublicationId, DefinitionId, ProfileId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (Revision != 1 || PolicyRevision is < 1 or long.MaxValue || ModeCode is < 1 or > 2
            || RecordedByHumanId == Guid.Empty || WriterGeneration is < 1 or long.MaxValue
            || !Digest(ProposedStateDigest) || !Digest(InventoryFingerprint) || !Digest(DatabaseConfigurationDigest)
            || string.IsNullOrWhiteSpace(DatabaseProviderCode) || DatabaseProviderCode.Length > 32
            || string.IsNullOrWhiteSpace(DatabaseProviderVersion) || DatabaseProviderVersion.Length > 256
            || Consumers.Count is < 1 or > 256)
            throw new ArgumentException("Deployment requires exact native definition, primary database and nonempty runtime coverage.");
        ModerationQualificationPolicySource.RequireValidity(RecordedAt, ValidUntil);
        var slots = new HashSet<(string, string)>();
        for (var index = 0; index < Consumers.Count; index++)
        {
            var consumer = Consumers[index];
            consumer.ValidatePersistedState();
            if (consumer.SourceId != Id || consumer.Ordinal != index
                || !slots.Add((consumer.BindingDigest, consumer.ConsumerId))
                || consumer.InstallationWriterGeneration >= WriterGeneration
                || consumer.ValidUntil < ValidUntil)
                throw new ArgumentException("Deployment coverage must be complete, unique, ordered and bounded by its native installations.");
        }
    }

    internal static bool Digest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    public override string ToString() => nameof(ModerationDeploymentSource);
}

/// <summary>One exact native consumer binding and deployed generation, not a test result.</summary>
public sealed class ModerationDeploymentConsumer
{
    private ModerationDeploymentConsumer() { }
    public Guid SourceId { get; private set; }
    public int Ordinal { get; private set; }
    public string ConsumerId { get; private set; } = string.Empty;
    public string BindingDigest { get; private set; } = string.Empty;
    public string ContractDigest { get; private set; } = string.Empty;
    public Guid InstallationId { get; private set; }
    public long InstallationRevision { get; private set; }
    public long InstallationWriterGeneration { get; private set; }
    public string RuntimeGeneration { get; private set; } = string.Empty;
    public string ProductArtifactDigest { get; private set; } = string.Empty;
    public DateTimeOffset ValidUntil { get; private set; }

    public static ModerationDeploymentConsumer Capture(Guid sourceId, int ordinal, string consumerId,
        string bindingDigest, string contractDigest, Guid installationId, long installationRevision,
        long installationWriterGeneration, string runtimeGeneration, string productArtifactDigest, DateTimeOffset validUntil)
    {
        var row = new ModerationDeploymentConsumer
        {
            SourceId = sourceId, Ordinal = ordinal, ConsumerId = consumerId, BindingDigest = bindingDigest,
            ContractDigest = contractDigest, InstallationId = installationId, InstallationRevision = installationRevision,
            InstallationWriterGeneration = installationWriterGeneration, RuntimeGeneration = runtimeGeneration,
            ProductArtifactDigest = productArtifactDigest, ValidUntil = validUntil
        };
        row.ValidatePersistedState();
        return row;
    }

    public void ValidatePersistedState()
    {
        ModerationHostBinding.RequireUuidV7(SourceId, nameof(SourceId));
        ModerationHostBinding.RequireUuidV7(InstallationId, nameof(InstallationId));
        if (Ordinal is < 0 or > 255 || string.IsNullOrWhiteSpace(ConsumerId) || ConsumerId.Length > 128
            || InstallationRevision != 1 || InstallationWriterGeneration is < 1 or long.MaxValue
            || !ModerationDeploymentSource.Digest(BindingDigest) || !ModerationDeploymentSource.Digest(ContractDigest)
            || !ModerationDeploymentSource.Digest(RuntimeGeneration) || !ModerationDeploymentSource.Digest(ProductArtifactDigest)
            || ValidUntil.Offset != TimeSpan.Zero || ValidUntil == DateTimeOffset.MinValue || ValidUntil == DateTimeOffset.MaxValue)
            throw new ArgumentException("Deployment coverage requires exact finite native installation and product artifact coordinates.");
    }
}
