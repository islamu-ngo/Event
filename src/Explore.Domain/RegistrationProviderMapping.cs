using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class RegistrationProviderCapability : ITenantEntity
{
    private RegistrationProviderCapability() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid RegistrationProviderBindingId { get; private set; }
    public RegistrationProviderBinding? Binding { get; private set; }
    public string ProviderCode { get; private set; } = string.Empty;
    public string DeploymentKind { get; private set; } = string.Empty;
    public string ApiVersion { get; private set; } = string.Empty;
    public string AdapterPolicyVersion { get; private set; } = string.Empty;
    public string ConformanceEvidenceRevision { get; private set; } = string.Empty;
    public string CapabilityCode { get; private set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public string TupleKey => string.Join('|', ProviderCode, DeploymentKind, ApiVersion, AdapterPolicyVersion, ConformanceEvidenceRevision, CapabilityCode);
    public static RegistrationProviderCapability Create(RegistrationProviderBinding binding, string providerCode, string deploymentKind, string apiVersion, string adapterPolicyVersion, string conformanceEvidenceRevision, string capabilityCode)
    {
        binding.EnsureDraft();
        return new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = binding.TenantId,
            RegistrationProviderBindingId = binding.Id,
            ProviderCode = Normalize(providerCode, nameof(providerCode), 100),
            DeploymentKind = Normalize(deploymentKind, nameof(deploymentKind), 100),
            ApiVersion = Normalize(apiVersion, nameof(apiVersion), 100),
            AdapterPolicyVersion = Normalize(adapterPolicyVersion, nameof(adapterPolicyVersion), 100),
            ConformanceEvidenceRevision = Normalize(conformanceEvidenceRevision, nameof(conformanceEvidenceRevision), 200),
            CapabilityCode = Normalize(capabilityCode, nameof(capabilityCode), 100)
        };
    }
    private static string Normalize(string value, string parameterName, int max) => (value?.Trim() ?? string.Empty) is { Length: > 0 } text && text.Length <= max ? text : throw new ArgumentException($"Value must be non-blank and at most {max} characters.", parameterName);
}

public sealed class RegistrationProviderFieldMapping : ITenantEntity
{
    private RegistrationProviderFieldMapping() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid RegistrationProviderBindingId { get; private set; }
    public RegistrationProviderBinding? Binding { get; private set; }
    public string PlatformFieldKey { get; private set; } = string.Empty;
    public string ProviderFieldKey { get; private set; } = string.Empty;
    public bool IsRequired { get; private set; }
    public bool IsDeleted { get; set; }
    public static RegistrationProviderFieldMapping Create(RegistrationProviderBinding binding, string platformFieldKey, string providerFieldKey, bool isRequired)
    {
        binding.EnsureDraft();
        return new() { Id = Guid.CreateVersion7(), TenantId = binding.TenantId, RegistrationProviderBindingId = binding.Id, PlatformFieldKey = Normalize(platformFieldKey, nameof(platformFieldKey), 200), ProviderFieldKey = Normalize(providerFieldKey, nameof(providerFieldKey), 200), IsRequired = isRequired };
    }
    private static string Normalize(string value, string parameterName, int max) => (value?.Trim() ?? string.Empty) is { Length: > 0 } text && text.Length <= max ? text : throw new ArgumentException($"Value must be non-blank and at most {max} characters.", parameterName);
}

public sealed class RegistrationProviderOptionMapping : ITenantEntity
{
    private RegistrationProviderOptionMapping() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid RegistrationProviderBindingId { get; private set; }
    public RegistrationProviderBinding? Binding { get; private set; }
    public Guid RegistrationProviderFieldMappingId { get; private set; }
    public string PlatformOptionKey { get; private set; } = string.Empty;
    public string ProviderOptionKey { get; private set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public static RegistrationProviderOptionMapping Create(RegistrationProviderBinding binding, RegistrationProviderFieldMapping fieldMapping, string platformOptionKey, string providerOptionKey)
    {
        binding.EnsureDraft();
        if (fieldMapping.TenantId != binding.TenantId || fieldMapping.RegistrationProviderBindingId != binding.Id) throw new ArgumentException("Option mapping field must belong to the binding.", nameof(fieldMapping));
        return new() { Id = Guid.CreateVersion7(), TenantId = binding.TenantId, RegistrationProviderBindingId = binding.Id, RegistrationProviderFieldMappingId = fieldMapping.Id, PlatformOptionKey = Normalize(platformOptionKey, nameof(platformOptionKey), 200), ProviderOptionKey = Normalize(providerOptionKey, nameof(providerOptionKey), 200) };
    }
    private static string Normalize(string value, string parameterName, int max) => (value?.Trim() ?? string.Empty) is { Length: > 0 } text && text.Length <= max ? text : throw new ArgumentException($"Value must be non-blank and at most {max} characters.", parameterName);
}

public sealed class RegistrationProviderSchemaRevision : ITenantEntity, IAuditableEntity
{
    private RegistrationProviderSchemaRevision() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid RegistrationProviderConnectionId { get; private set; }
    public RegistrationProviderConnection? Connection { get; private set; }
    public int SchemaAuthorityId { get; private set; }
    public RegistrationEvidenceHash RevisionHash { get; private set; } = null!;
    public string ProviderSurveyId { get; private set; } = string.Empty;
    public string? ProviderSurveyRevisionId { get; private set; }
    public string ProviderSnapshotJson { get; private set; } = string.Empty;
    public string ProviderSnapshotSha256Hash { get; private set; } = string.Empty;
    public int DriftClassId { get; private set; }
    public DateTime ObservedAt { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public static RegistrationProviderSchemaRevision Create(
        Guid tenantId,
        Guid connectionId,
        RegistrationProviderSchemaAuthorityEnum authority,
        RegistrationEvidenceHash revisionHash,
        string providerSurveyId,
        string? providerSurveyRevisionId,
        string providerSnapshotJson,
        string providerSnapshotSha256Hash,
        RegistrationProviderDriftClassEnum driftClass,
        DateTime observedAt)
    {
        if (tenantId == Guid.Empty || connectionId == Guid.Empty || !Enum.IsDefined(authority) || !Enum.IsDefined(driftClass)) throw new ArgumentException("Schema revision identities and lookup values must be valid.");
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSnapshotJson);
        if (providerSnapshotSha256Hash.Length != 64 || providerSnapshotSha256Hash.Any(value => !Uri.IsHexDigit(value))) throw new ArgumentException("Provider snapshot hash must be canonical lowercase SHA-256 hex.", nameof(providerSnapshotSha256Hash));
        return new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            RegistrationProviderConnectionId = connectionId,
            SchemaAuthorityId = (int)authority,
            RevisionHash = revisionHash,
            ProviderSurveyId = Normalize(providerSurveyId, nameof(providerSurveyId), 200),
            ProviderSurveyRevisionId = string.IsNullOrWhiteSpace(providerSurveyRevisionId) ? null : Normalize(providerSurveyRevisionId, nameof(providerSurveyRevisionId), 200),
            ProviderSnapshotJson = providerSnapshotJson,
            ProviderSnapshotSha256Hash = providerSnapshotSha256Hash.ToLowerInvariant(),
            DriftClassId = (int)driftClass,
            ObservedAt = RegistrationProviderConnection.EnsureUtc(observedAt, nameof(observedAt)),
            CreatedAt = RegistrationProviderConnection.EnsureUtc(observedAt, nameof(observedAt))
        };
    }

    private static string Normalize(string value, string parameterName, int max) => (value?.Trim() ?? string.Empty) is { Length: > 0 } text && text.Length <= max ? text : throw new ArgumentException($"Value must be non-blank and at most {max} characters.", parameterName);
}
