using Explore.Domain.Interfaces;

namespace Explore.Domain;

public enum ConfigurationManifestTenantResultStatus
{
    Created = 1,
    SkippedExisting = 2
}

public sealed class ConfigurationManifestTenantResult : ITenantEntity
{
    public const int MaxKeyLength = 200;
    public const int MaxChangedKeyNamesLength = 4096;
    public const int MaxReasonCodeLength = 200;

    public const string CreatedReasonCode = "configuration_manifest_tenant_created";
    public const string SkippedExistingReasonCode =
        "configuration_manifest_tenant_existing";

    private string _changedSettingKeyNames = string.Empty;
    private string _changedDocumentKeyNames = string.Empty;

    private ConfigurationManifestTenantResult()
    {
    }

    public Guid Id { get; private set; }
    public Guid OperationId { get; private set; }
    public ConfigurationManifestOperation Operation { get; private set; } = null!;
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; private set; } = null!;
    public ConfigurationManifestTenantResultStatus Status { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public DateTime CompletedAt { get; private set; }

    public IReadOnlyList<string> ChangedSettingKeyNames => Split(_changedSettingKeyNames);
    public IReadOnlyList<string> ChangedDocumentKeyNames => Split(_changedDocumentKeyNames);
    public IReadOnlyList<string> ChangedKeyNames => ChangedSettingKeyNames
        .Concat(ChangedDocumentKeyNames)
        .Order(StringComparer.Ordinal)
        .ToArray();

    public static ConfigurationManifestTenantResult Create(
        Guid operationId,
        Guid tenantId,
        ConfigurationManifestTenantResultStatus status,
        IEnumerable<string> changedSettingKeyNames,
        IEnumerable<string> changedDocumentKeyNames,
        DateTime completedAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(operationId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(changedSettingKeyNames);
        ArgumentNullException.ThrowIfNull(changedDocumentKeyNames);
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (completedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Manifest tenant-result timestamp must use UTC kind.", nameof(completedAt));
        }

        string settings = NormalizeKeyNames(changedSettingKeyNames, nameof(changedSettingKeyNames));
        string documents = NormalizeKeyNames(changedDocumentKeyNames, nameof(changedDocumentKeyNames));
        if (status == ConfigurationManifestTenantResultStatus.SkippedExisting
            && (settings.Length != 0 || documents.Length != 0))
        {
            throw new ArgumentException("Skipped tenants cannot report changed configuration keys.");
        }

        return new ConfigurationManifestTenantResult
        {
            Id = Guid.CreateVersion7(),
            OperationId = operationId,
            TenantId = tenantId,
            Status = status,
            ReasonCode = status == ConfigurationManifestTenantResultStatus.Created
                ? CreatedReasonCode
                : SkippedExistingReasonCode,
            _changedSettingKeyNames = settings,
            _changedDocumentKeyNames = documents,
            CompletedAt = completedAt
        };
    }

    private static string NormalizeKeyNames(IEnumerable<string> values, string parameterName)
    {
        string[] normalized = values
            .Select(value => NormalizeKey(value, parameterName))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string storage = string.Join('\n', normalized);
        if (storage.Length > MaxChangedKeyNamesLength)
        {
            throw new ArgumentException(
                $"Changed key names cannot exceed {MaxChangedKeyNamesLength} characters.",
                parameterName);
        }

        return storage;
    }

    private static string NormalizeKey(string value, string parameterName)
    {
        string? normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)
            || normalized.Length > MaxKeyLength
            || normalized.Any(character => character is not (
                >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '.'
                or '_'
                or '-')))
        {
            throw new ArgumentException("Changed keys must be bounded canonical identifiers.", parameterName);
        }

        return normalized;
    }

    private static IReadOnlyList<string> Split(string storage) =>
        storage.Length == 0
            ? []
            : storage.Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
