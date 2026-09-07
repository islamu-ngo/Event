namespace Explore.Domain;

public enum ConfigurationManifestAuditMode
{
    ValidateOnly = 1,
    Bootstrap = 2
}

public enum ConfigurationManifestOperationStatus
{
    Validated = 1,
    Applied = 2,
    Failed = 3
}

public sealed class ConfigurationManifestOperation
{
    public const int MaxApiVersionLength = 100;
    public const int MaxKindLength = 100;
    public const int MaxManifestNameLength = 100;
    public const int DigestLength = 64;
    public const int MaxReasonCodeLength = 200;
    public const int MaxReasonLength = 500;
    public const int MaxChangedKeyNamesLength = 4096;

    private ConfigurationManifestOperation()
    {
    }

    public Guid Id { get; private set; }
    public ConfigurationManifestAuditMode Mode { get; private set; }
    public string ApiVersion { get; private set; } = string.Empty;
    public string Kind { get; private set; } = string.Empty;
    public string ManifestName { get; private set; } = string.Empty;
    public string Digest { get; private set; } = string.Empty;
    public string? InstanceSectionDigest { get; private set; }
    public int? BootstrapGeneration { get; private set; }
    public ConfigurationManifestOperationStatus Status { get; private set; }
    public int RequestedTenantCount { get; private set; }
    public int CreatedTenantCount { get; private set; }
    public int SkippedExistingTenantCount { get; private set; }
    public int FailedTenantCount { get; private set; }
    public string? ReasonCode { get; private set; }
    public string? Reason { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime CompletedAt { get; private set; }
    private string _instanceChangedSettingKeyNames = string.Empty;
    private string _instanceChangedDocumentKeyNames = string.Empty;

    public IReadOnlyList<string> InstanceChangedSettingKeyNames =>
        Split(_instanceChangedSettingKeyNames);
    public IReadOnlyList<string> InstanceChangedDocumentKeyNames =>
        Split(_instanceChangedDocumentKeyNames);

    public static ConfigurationManifestOperation Create(
        ConfigurationManifestAuditMode mode,
        string apiVersion,
        string kind,
        string manifestName,
        string digest,
        ConfigurationManifestOperationStatus status,
        int requestedTenantCount,
        int createdTenantCount,
        int skippedExistingTenantCount,
        int failedTenantCount,
        string? reasonCode,
        string? reason,
        DateTime startedAt,
        DateTime completedAt,
        string? instanceSectionDigest = null,
        int? bootstrapGeneration = null,
        IEnumerable<string>? instanceChangedSettingKeyNames = null,
        IEnumerable<string>? instanceChangedDocumentKeyNames = null)
        => Create(
            Guid.CreateVersion7(),
            mode,
            apiVersion,
            kind,
            manifestName,
            digest,
            status,
            requestedTenantCount,
            createdTenantCount,
            skippedExistingTenantCount,
            failedTenantCount,
            reasonCode,
            reason,
            startedAt,
            completedAt,
            instanceSectionDigest,
            bootstrapGeneration,
            instanceChangedSettingKeyNames,
            instanceChangedDocumentKeyNames);

    public static ConfigurationManifestOperation Create(
        Guid operationId,
        ConfigurationManifestAuditMode mode,
        string apiVersion,
        string kind,
        string manifestName,
        string digest,
        ConfigurationManifestOperationStatus status,
        int requestedTenantCount,
        int createdTenantCount,
        int skippedExistingTenantCount,
        int failedTenantCount,
        string? reasonCode,
        string? reason,
        DateTime startedAt,
        DateTime completedAt,
        string? instanceSectionDigest = null,
        int? bootstrapGeneration = null,
        IEnumerable<string>? instanceChangedSettingKeyNames = null,
        IEnumerable<string>? instanceChangedDocumentKeyNames = null)
    {
        if (operationId == Guid.Empty || operationId.Version != 7)
        {
            throw new ArgumentException("Manifest operation identity must be UUIDv7.", nameof(operationId));
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        ValidateCounts(
            mode,
            status,
            requestedTenantCount,
            createdTenantCount,
            skippedExistingTenantCount,
            failedTenantCount);
        ValidateTimestamps(startedAt, completedAt);

        bool failed = status == ConfigurationManifestOperationStatus.Failed;
        string? normalizedReasonCode = NormalizeOptional(
            reasonCode,
            MaxReasonCodeLength,
            nameof(reasonCode));
        string? normalizedReason = NormalizeOptional(reason, MaxReasonLength, nameof(reason));
        if (failed != (normalizedReasonCode is not null && normalizedReason is not null))
        {
            throw new ArgumentException("Only failed manifest operations require a safe reason code and reason.");
        }

        string? normalizedInstanceSectionDigest =
            string.IsNullOrWhiteSpace(instanceSectionDigest)
                ? null
                : NormalizeDigest(instanceSectionDigest);
        if ((normalizedInstanceSectionDigest is null)
            != (bootstrapGeneration is null))
        {
            throw new ArgumentException(
                "Manifest bootstrap digest and generation must be recorded together.");
        }

        if (bootstrapGeneration is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bootstrapGeneration),
                "Manifest bootstrap generation must be positive.");
        }

        if (status == ConfigurationManifestOperationStatus.Applied
            && mode == ConfigurationManifestAuditMode.Bootstrap
            && normalizedInstanceSectionDigest is null)
        {
            throw new ArgumentException(
                "Applied bootstrap operations require instance bootstrap state.");
        }

        string instanceSettings = NormalizeKeyNames(
            instanceChangedSettingKeyNames ?? [],
            nameof(instanceChangedSettingKeyNames));
        string instanceDocuments = NormalizeKeyNames(
            instanceChangedDocumentKeyNames ?? [],
            nameof(instanceChangedDocumentKeyNames));
        if (status != ConfigurationManifestOperationStatus.Applied
            && (instanceSettings.Length != 0 || instanceDocuments.Length != 0))
        {
            throw new ArgumentException(
                "Only applied manifest operations can report instance changes.");
        }

        return new ConfigurationManifestOperation
        {
            Id = operationId,
            Mode = mode,
            ApiVersion = NormalizeRequired(apiVersion, MaxApiVersionLength, nameof(apiVersion)),
            Kind = NormalizeRequired(kind, MaxKindLength, nameof(kind)),
            ManifestName = NormalizeRequired(manifestName, MaxManifestNameLength, nameof(manifestName)),
            Digest = NormalizeDigest(digest),
            InstanceSectionDigest = normalizedInstanceSectionDigest,
            BootstrapGeneration = bootstrapGeneration,
            Status = status,
            RequestedTenantCount = requestedTenantCount,
            CreatedTenantCount = createdTenantCount,
            SkippedExistingTenantCount = skippedExistingTenantCount,
            FailedTenantCount = failedTenantCount,
            ReasonCode = normalizedReasonCode,
            Reason = normalizedReason,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            _instanceChangedSettingKeyNames = instanceSettings,
            _instanceChangedDocumentKeyNames = instanceDocuments
        };
    }

    private static void ValidateCounts(
        ConfigurationManifestAuditMode mode,
        ConfigurationManifestOperationStatus status,
        int requested,
        int created,
        int skipped,
        int failed)
    {
        if (requested < 0 || created < 0 || skipped < 0 || failed < 0
            || created > requested || skipped > requested || failed > requested)
        {
            throw new ArgumentOutOfRangeException(nameof(requested), "Manifest tenant counts must be consistent.");
        }

        bool valid = status switch
        {
            ConfigurationManifestOperationStatus.Validated =>
                mode == ConfigurationManifestAuditMode.ValidateOnly
                && created == 0 && skipped == 0 && failed == 0,
            ConfigurationManifestOperationStatus.Applied =>
                mode == ConfigurationManifestAuditMode.Bootstrap
                && created + skipped == requested && failed == 0,
            ConfigurationManifestOperationStatus.Failed =>
                created == 0 && skipped == 0,
            _ => false
        };

        if (!valid)
        {
            throw new ArgumentException("Manifest operation mode, status, and tenant counts are inconsistent.");
        }
    }

    private static void ValidateTimestamps(DateTime startedAt, DateTime completedAt)
    {
        if (startedAt.Kind != DateTimeKind.Utc || completedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Manifest operation timestamps must use UTC kind.");
        }

        if (completedAt < startedAt)
        {
            throw new ArgumentException("Manifest operation completion cannot precede its start.");
        }
    }

    private static string NormalizeDigest(string value)
    {
        string normalized = NormalizeRequired(value, DigestLength, nameof(value));
        if (normalized.Length != DigestLength
            || normalized.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new ArgumentException("Manifest digest must be lowercase SHA-256 hexadecimal.", nameof(value));
        }

        return normalized;
    }

    private static string NormalizeKeyNames(
        IEnumerable<string> values,
        string parameterName)
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
            || normalized.Length > ConfigurationManifestTenantResult
                .MaxKeyLength
            || normalized.Any(character => character is not (
                >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '.'
                or '_'
                or '-')))
        {
            throw new ArgumentException(
                "Changed keys must be bounded canonical identifiers.",
                parameterName);
        }

        return normalized;
    }

    private static string NormalizeRequired(string value, int maximumLength, string parameterName) =>
        NormalizeOptional(value, maximumLength, parameterName)
        ?? throw new ArgumentException("A bounded non-empty value is required.", parameterName);

    private static string? NormalizeOptional(string? value, int maximumLength, string parameterName)
    {
        string? normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"Value cannot exceed {maximumLength} characters.", parameterName);
        }

        return normalized;
    }

    private static string[] Split(string storage) =>
        storage.Length == 0
            ? []
            : storage.Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
