namespace Explore.Application.Features.ConfigurationManifest.Importing;

using System.Collections.Frozen;

public static class ConfigurationImportFailureCodes
{
    public const string Cancelled = "configuration_import_cancelled";
    public const string Expired = "configuration_import_expired";
    public const string Replayed = "configuration_import_replayed";
    public const string StalePreview = "configuration_import_stale_preview";
    public const string TargetMismatch = "configuration_import_target_mismatch";
    public const string TokenInvalid = "configuration_import_token_invalid";
    public const string TooLarge = "configuration_import_too_large";
    public const string ArtifactMissing = "configuration_import_artifact_missing";
    public const string ArtifactIntegrityInvalid =
        "configuration_import_artifact_integrity_invalid";
    public const string ContractInvalid = "configuration_import_contract_invalid";
    public const string ApplyBlocked = "configuration_import_apply_blocked";
    public const string ApplyFailed = "configuration_import_apply_failed";
    public const string SnapshotUnavailable =
        "configuration_import_snapshot_unavailable";
    public const string RollbackUnavailable =
        "configuration_import_rollback_unavailable";
}

public sealed record ConfigurationImportSessionEvidence
{
    public ConfigurationImportSessionEvidence(
        Guid sessionId,
        string scopeCode,
        string artifactDigest,
        string outcomeCode,
        int itemCount,
        int blockingCount,
        DateTime occurredAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        ConfigurationImportContractGuard.ValidateDigest(
            artifactDigest,
            nameof(artifactDigest));
        ConfigurationImportContractGuard.RequireUtc(occurredAt, nameof(occurredAt));
        ValidateCounts(itemCount, blockingCount);
        SessionId = sessionId;
        ScopeCode = ConfigurationImportSafeCode.Normalize(
            scopeCode,
            nameof(scopeCode));
        ArtifactDigest = artifactDigest;
        OutcomeCode = ConfigurationImportSafeCode.Normalize(
            outcomeCode,
            nameof(outcomeCode));
        ItemCount = itemCount;
        BlockingCount = blockingCount;
        OccurredAt = occurredAt;
    }

    public Guid SessionId { get; }
    public string ScopeCode { get; }
    public string ArtifactDigest { get; }
    public string OutcomeCode { get; }
    public int ItemCount { get; }
    public int BlockingCount { get; }
    public DateTime OccurredAt { get; }

    private static void ValidateCounts(int itemCount, int blockingCount)
    {
        if (itemCount is < 0 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(itemCount));
        if (blockingCount < 0 || blockingCount > itemCount)
            throw new ArgumentOutOfRangeException(nameof(blockingCount));
    }
}

public sealed record ConfigurationImportObservabilityEvent
{
    public ConfigurationImportObservabilityEvent(
        string scopeCode,
        string artifactKindCode,
        string outcomeCode,
        int itemCount,
        int blockingCount,
        long elapsedMilliseconds)
    {
        if (itemCount is < 0 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(itemCount));
        if (blockingCount < 0 || blockingCount > itemCount)
            throw new ArgumentOutOfRangeException(nameof(blockingCount));
        if (elapsedMilliseconds is < 0 or > 3_600_000)
            throw new ArgumentOutOfRangeException(nameof(elapsedMilliseconds));
        ScopeCode = ConfigurationImportSafeCode.Normalize(
            scopeCode,
            nameof(scopeCode));
        ArtifactKindCode = ConfigurationImportSafeCode.Normalize(
            artifactKindCode,
            nameof(artifactKindCode));
        OutcomeCode = ConfigurationImportSafeCode.Normalize(
            outcomeCode,
            nameof(outcomeCode));
        ItemCount = itemCount;
        BlockingCount = blockingCount;
        ElapsedMilliseconds = elapsedMilliseconds;
    }

    public string ScopeCode { get; }
    public string ArtifactKindCode { get; }
    public string OutcomeCode { get; }
    public int ItemCount { get; }
    public int BlockingCount { get; }
    public long ElapsedMilliseconds { get; }
}

public static class ConfigurationImportObservabilityContract
{
    public static IReadOnlyDictionary<string, object?> CreateLogState(
        ConfigurationImportObservabilityEvent value) =>
        Create(value);

    public static IReadOnlyDictionary<string, object?> CreateMetricTags(
        ConfigurationImportObservabilityEvent value) =>
        Create(value);

    public static IReadOnlyDictionary<string, object?> CreateTraceTags(
        ConfigurationImportObservabilityEvent value) =>
        Create(value);

    private static IReadOnlyDictionary<string, object?> Create(
        ConfigurationImportObservabilityEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["scope"] = value.ScopeCode,
            ["artifact_kind"] = value.ArtifactKindCode,
            ["outcome"] = value.OutcomeCode,
            ["item_count"] = value.ItemCount,
            ["blocking_count"] = value.BlockingCount,
            ["elapsed_ms"] = value.ElapsedMilliseconds
        }.ToFrozenDictionary(StringComparer.Ordinal);
    }
}

internal static class ConfigurationImportSafeCode
{
    public static string Normalize(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string normalized = value.Trim();
        if (normalized.Length > 100
            || normalized.Any(character =>
                !char.IsAsciiLetterLower(character)
                && !char.IsAsciiDigit(character)
                && character is not '_' and not '-' and not '.'))
        {
            throw new ArgumentException(
                "Configuration import observability code is invalid.",
                parameterName);
        }

        return normalized;
    }
}
