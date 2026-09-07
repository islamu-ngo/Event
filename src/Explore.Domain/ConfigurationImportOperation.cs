namespace Explore.Domain;

public enum ConfigurationImportOperationKind
{
    Apply = 1,
    ForwardRollback = 2
}

public enum ConfigurationImportOperationStatus
{
    Prepared = 1,
    Applied = 2,
    Failed = 3,
    RolledBack = 4
}

public sealed class ConfigurationImportOperation
{
    public const int MaximumAuthorityKeyLength = 64;
    public const int MaximumSectionKeysLength = 2_048;
    public const int MaximumOmittedSectionKeysLength = 4_096;
    public const int MaximumFailureCodeLength = 200;
    public const int MaximumFailureReasonLength = 500;

    private ConfigurationImportOperation()
    {
    }

    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public ConfigurationImportOperationKind Kind { get; private set; }
    public ConfigurationImportOperationStatus Status { get; private set; }
    public string TargetAuthorityKey { get; private set; } = string.Empty;
    public Guid? TargetTenantId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public Guid? SourceOperationId { get; private set; }
    public string ArtifactDigest { get; private set; } = string.Empty;
    public string TargetRevisionDigest { get; private set; } = string.Empty;
    public string SelectedSectionsDigest { get; private set; } = string.Empty;
    public string MappingDigest { get; private set; } = string.Empty;
    public string ApprovalDigest { get; private set; } = string.Empty;
    public int ApplyMode { get; private set; }
    public Guid? SnapshotArtifactHandleId { get; private set; }
    public string? SnapshotDigest { get; private set; }
    public DateTime? SnapshotExpiresAt { get; private set; }
    public Guid? EffectOutboxId { get; private set; }
    public bool FidelityVerified { get; private set; }
    public string FidelityDigest { get; private set; } = string.Empty;
    public string? FailureCode { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    private string _selectedSectionKeys = string.Empty;
    private string _omittedSectionKeys = string.Empty;

    public IReadOnlyList<string> SelectedSectionKeys =>
        _selectedSectionKeys.Length == 0
            ? []
            : _selectedSectionKeys.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries);
    public IReadOnlyList<string> OmittedSectionKeys =>
        _omittedSectionKeys.Length == 0
            ? []
            : _omittedSectionKeys.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries);

    public static ConfigurationImportOperation CreateApplied(
        Guid operationId,
        Guid sessionId,
        string targetAuthorityKey,
        Guid? targetTenantId,
        Guid actorUserId,
        Guid? sourceOperationId,
        string artifactDigest,
        string targetRevisionDigest,
        string selectedSectionsDigest,
        string mappingDigest,
        string approvalDigest,
        int applyMode,
        IEnumerable<string> selectedSectionKeys,
        Guid snapshotArtifactHandleId,
        string snapshotDigest,
        DateTime snapshotExpiresAt,
        Guid effectOutboxId,
        bool fidelityVerified,
        string fidelityDigest,
        IEnumerable<string> omittedSectionKeys,
        DateTime startedAt,
        DateTime completedAt) =>
        Create(
            operationId,
            sessionId,
            sourceOperationId.HasValue
                ? ConfigurationImportOperationKind.ForwardRollback
                : ConfigurationImportOperationKind.Apply,
            sourceOperationId.HasValue
                ? ConfigurationImportOperationStatus.RolledBack
                : ConfigurationImportOperationStatus.Applied,
            targetAuthorityKey,
            targetTenantId,
            actorUserId,
            sourceOperationId,
            artifactDigest,
            targetRevisionDigest,
            selectedSectionsDigest,
            mappingDigest,
            approvalDigest,
            applyMode,
            selectedSectionKeys,
            snapshotArtifactHandleId,
            snapshotDigest,
            snapshotExpiresAt,
            effectOutboxId,
            fidelityVerified,
            fidelityDigest,
            omittedSectionKeys,
            failureCode: null,
            failureReason: null,
            startedAt,
            completedAt);

    public static ConfigurationImportOperation CreateFailed(
        Guid operationId,
        Guid sessionId,
        string targetAuthorityKey,
        Guid? targetTenantId,
        Guid actorUserId,
        Guid? sourceOperationId,
        string artifactDigest,
        int applyMode,
        IEnumerable<string> selectedSectionKeys,
        string failureCode,
        string failureReason,
        DateTime startedAt,
        DateTime completedAt) =>
        Create(
            operationId,
            sessionId,
            sourceOperationId.HasValue
                ? ConfigurationImportOperationKind.ForwardRollback
                : ConfigurationImportOperationKind.Apply,
            ConfigurationImportOperationStatus.Failed,
            targetAuthorityKey,
            targetTenantId,
            actorUserId,
            sourceOperationId,
            artifactDigest,
            EmptyDigest,
            EmptyDigest,
            EmptyDigest,
            EmptyDigest,
            applyMode,
            selectedSectionKeys,
            snapshotArtifactHandleId: null,
            snapshotDigest: null,
            snapshotExpiresAt: null,
            effectOutboxId: null,
            fidelityVerified: false,
            fidelityDigest: EmptyDigest,
            omittedSectionKeys: [],
            failureCode,
            failureReason,
            startedAt,
            completedAt);

    private const string EmptyDigest =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private static ConfigurationImportOperation Create(
        Guid operationId,
        Guid sessionId,
        ConfigurationImportOperationKind kind,
        ConfigurationImportOperationStatus status,
        string targetAuthorityKey,
        Guid? targetTenantId,
        Guid actorUserId,
        Guid? sourceOperationId,
        string artifactDigest,
        string targetRevisionDigest,
        string selectedSectionsDigest,
        string mappingDigest,
        string approvalDigest,
        int applyMode,
        IEnumerable<string> selectedSectionKeys,
        Guid? snapshotArtifactHandleId,
        string? snapshotDigest,
        DateTime? snapshotExpiresAt,
        Guid? effectOutboxId,
        bool fidelityVerified,
        string fidelityDigest,
        IEnumerable<string> omittedSectionKeys,
        string? failureCode,
        string? failureReason,
        DateTime startedAt,
        DateTime completedAt)
    {
        RequireVersion7(operationId, nameof(operationId));
        RequireVersion7(sessionId, nameof(sessionId));
        ArgumentOutOfRangeException.ThrowIfEqual(actorUserId, Guid.Empty);
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (targetTenantId == Guid.Empty || sourceOperationId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(targetTenantId));
        if (sourceOperationId.HasValue)
            RequireVersion7(sourceOperationId.Value, nameof(sourceOperationId));
        RequireUtc(startedAt, nameof(startedAt));
        RequireUtc(completedAt, nameof(completedAt));
        if (completedAt < startedAt)
            throw new ArgumentOutOfRangeException(nameof(completedAt));

        string authority = Normalize(
            targetAuthorityKey,
            MaximumAuthorityKeyLength,
            nameof(targetAuthorityKey));
        if ((targetTenantId is null) !=
            string.Equals(authority, "instance", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Configuration import target authority is inconsistent.",
                nameof(targetAuthorityKey));
        }

        string sectionStorage = string.Join(
            '\n',
            selectedSectionKeys
                .Select(section => Normalize(section, 200, nameof(selectedSectionKeys)))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
        if (sectionStorage.Length is 0 or > MaximumSectionKeysLength)
            throw new ArgumentOutOfRangeException(nameof(selectedSectionKeys));
        string omittedStorage = string.Join(
            '\n',
            omittedSectionKeys
                .Select(section => Normalize(section, 200, nameof(omittedSectionKeys)))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
        if (omittedStorage.Length > MaximumOmittedSectionKeysLength)
            throw new ArgumentOutOfRangeException(nameof(omittedSectionKeys));

        bool failed = status == ConfigurationImportOperationStatus.Failed;
        string? safeCode = string.IsNullOrWhiteSpace(failureCode)
            ? null
            : Normalize(
                failureCode,
                MaximumFailureCodeLength,
                nameof(failureCode));
        string? safeReason = string.IsNullOrWhiteSpace(failureReason)
            ? null
            : Normalize(
                failureReason,
                MaximumFailureReasonLength,
                nameof(failureReason));
        if (failed != (safeCode is not null && safeReason is not null))
            throw new ArgumentException("Only failed imports record safe failure evidence.");

        bool completed = status is ConfigurationImportOperationStatus.Applied
            or ConfigurationImportOperationStatus.RolledBack;
        if (completed)
        {
            if (!snapshotArtifactHandleId.HasValue
                || !snapshotExpiresAt.HasValue
                || !effectOutboxId.HasValue)
            {
                throw new ArgumentException(
                    "Completed imports require snapshot and outbox evidence.");
            }
            RequireVersion7(snapshotArtifactHandleId.Value, nameof(snapshotArtifactHandleId));
            RequireVersion7(effectOutboxId.Value, nameof(effectOutboxId));
            RequireUtc(snapshotExpiresAt.Value, nameof(snapshotExpiresAt));
            if (!fidelityVerified)
                throw new ArgumentException(
                    "Completed imports require verified target fidelity.");
        }

        return new ConfigurationImportOperation
        {
            Id = operationId,
            SessionId = sessionId,
            Kind = kind,
            Status = status,
            TargetAuthorityKey = authority,
            TargetTenantId = targetTenantId,
            ActorUserId = actorUserId,
            SourceOperationId = sourceOperationId,
            ArtifactDigest = Digest(artifactDigest, nameof(artifactDigest)),
            TargetRevisionDigest = Digest(
                targetRevisionDigest,
                nameof(targetRevisionDigest)),
            SelectedSectionsDigest = Digest(
                selectedSectionsDigest,
                nameof(selectedSectionsDigest)),
            MappingDigest = Digest(mappingDigest, nameof(mappingDigest)),
            ApprovalDigest = Digest(approvalDigest, nameof(approvalDigest)),
            ApplyMode = applyMode,
            SnapshotArtifactHandleId = snapshotArtifactHandleId,
            SnapshotDigest = snapshotDigest is null
                ? null
                : Digest(snapshotDigest, nameof(snapshotDigest)),
            SnapshotExpiresAt = snapshotExpiresAt,
            EffectOutboxId = effectOutboxId,
            FidelityVerified = fidelityVerified,
            FidelityDigest = Digest(fidelityDigest, nameof(fidelityDigest)),
            FailureCode = safeCode,
            FailureReason = safeReason,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            _selectedSectionKeys = sectionStorage,
            _omittedSectionKeys = omittedStorage
        };
    }

    private static string Digest(string value, string parameterName)
    {
        string digest = Normalize(value, 64, parameterName);
        if (digest.Length != 64 || digest.Any(character =>
                character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new ArgumentException("Lowercase SHA-256 digest required.", parameterName);
        }
        return digest;
    }

    private static string Normalize(
        string value,
        int maximumLength,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentOutOfRangeException(parameterName);
        return normalized;
    }

    private static void RequireVersion7(Guid value, string parameterName)
    {
        if (value == Guid.Empty || value.Version != 7)
            throw new ArgumentException("UUIDv7 identity required.", parameterName);
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("UTC timestamp required.", parameterName);
    }
}
