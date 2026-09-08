namespace Explore.Application.Features.ConfigurationManifest.Importing;

using System.Security.Cryptography;
using Explore.Application.Contracts.Persistence;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public static class ConfigurationImportSessionLimits
{
    public const int MaximumArtifactBytes =
        ConfigurationPortabilityContentLimits.MaximumArtifactUtf8Bytes;

    public static TimeSpan DefaultSessionLifetime { get; } =
        TimeSpan.FromMinutes(30);

    public static TimeSpan MaximumSessionLifetime { get; } =
        TimeSpan.FromHours(1);

    public static TimeSpan SnapshotRetention { get; } =
        TimeSpan.FromDays(30);
}

public enum ConfigurationImportScope
{
    Instance = 1,
    Tenant = 2
}

public enum ConfigurationImportSessionState
{
    Uploaded = 1,
    PreviewReady = 2,
    Cancelled = 3,
    Expired = 4,
    Consumed = 5
}

public sealed record ConfigurationImportTarget
{
    private ConfigurationImportTarget(
        ConfigurationImportScope scope,
        Guid? tenantId)
    {
        Scope = scope;
        TenantId = tenantId;
    }

    public ConfigurationImportScope Scope { get; }
    public Guid? TenantId { get; }

    public static ConfigurationImportTarget ForInstance() =>
        new(ConfigurationImportScope.Instance, tenantId: null);

    public static ConfigurationImportTarget ForTenant(Guid tenantId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        return new(ConfigurationImportScope.Tenant, tenantId);
    }

    public string AuthorityKey => Scope switch
    {
        ConfigurationImportScope.Instance => "instance",
        ConfigurationImportScope.Tenant when TenantId is { } tenantId =>
            $"tenant:{tenantId:N}",
        _ => throw new InvalidOperationException(
            "Configuration import target is inconsistent.")
    };
}

public sealed record ConfigurationImportArtifactHandle
{
    public ConfigurationImportArtifactHandle(Guid id)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        Id = id;
    }

    public Guid Id { get; }
}

public sealed record ConfigurationImportArtifactReference
{
    public ConfigurationImportArtifactReference(
        ConfigurationImportArtifactHandle handle,
        string sha256Digest,
        int byteLength,
        DateTime expiresAt)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ConfigurationImportContractGuard.ValidateDigest(
            sha256Digest,
            nameof(sha256Digest));
        if (byteLength is < 1
            or > ConfigurationImportSessionLimits.MaximumArtifactBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        }

        ConfigurationImportContractGuard.RequireUtc(expiresAt, nameof(expiresAt));
        Handle = handle;
        Sha256Digest = sha256Digest;
        ByteLength = byteLength;
        ExpiresAt = expiresAt;
    }

    public ConfigurationImportArtifactHandle Handle { get; }
    public string Sha256Digest { get; }
    public int ByteLength { get; }
    public DateTime ExpiresAt { get; }
}

public interface IConfigurationImportArtifactStore
{
    Task<ConfigurationImportArtifactReference> StoreAsync(
        ConfigurationImportArtifactHandle handle,
        ReadOnlyMemory<byte> artifact,
        DateTime createdAt,
        DateTime expiresAt,
        CancellationToken cancellationToken);

    Task<ReadOnlyMemory<byte>> ReadAsync(
        ConfigurationImportArtifactHandle handle,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        ConfigurationImportArtifactHandle handle,
        CancellationToken cancellationToken);

    Task<int> DeleteExpiredAsync(
        DateTime occurredAt,
        int maximumCount,
        CancellationToken cancellationToken);
}

public interface IConfigurationImportSessionRepository
{
    Task AddAsync(
        ConfigurationImportSession session,
        CancellationToken cancellationToken);

    Task<ConfigurationImportSession?> GetForUpdateAsync(
        Guid sessionId,
        ConfigurationImportTarget target,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConfigurationImportSession>> ListExpiredForUpdateAsync(
        DateTime occurredAt,
        int maximumCount,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        ConfigurationImportSession session,
        CancellationToken cancellationToken);
}

public sealed class ConfigurationImportSession
{
    private ConfigurationImportSession()
    {
    }

    public Guid SessionId { get; private set; }
    public ConfigurationImportScope TargetScope { get; private set; }
    public Guid? TargetTenantId { get; private set; }
    public string TargetAuthorityKey { get; private set; } = string.Empty;
    public Guid ArtifactHandleId { get; private set; }
    public string ArtifactDigest { get; private set; } = string.Empty;
    public int ArtifactByteLength { get; private set; }
    public DateTime ArtifactExpiresAt { get; private set; }
    public string AccessTokenDigest { get; private set; } = string.Empty;
    public ConfigurationImportSessionState State { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public DateTime? ConsumedAt { get; private set; }
    public string? PreviewArtifactDigest { get; private set; }
    public string? PreviewTargetRevisionDigest { get; private set; }
    public string? PreviewSelectedSectionsDigest { get; private set; }
    public string? PreviewMappingDigest { get; private set; }
    public string? PreviewRequiredApprovalDigest { get; private set; }
    public ConfigurationImportApplyMode? PreviewApplyMode { get; private set; }
    public DateTime? PreviewExpiresAt { get; private set; }
    public long Revision { get; private set; }

    public ConfigurationImportTarget Target =>
        TargetScope == ConfigurationImportScope.Instance
            ? ConfigurationImportTarget.ForInstance()
            : ConfigurationImportTarget.ForTenant(
                TargetTenantId
                ?? throw new InvalidOperationException(
                    "Tenant import session has no tenant."));

    public ConfigurationImportArtifactReference Artifact =>
        new(
            new ConfigurationImportArtifactHandle(ArtifactHandleId),
            ArtifactDigest,
            ArtifactByteLength,
            ArtifactExpiresAt);

    public ConfigurationImportPreviewBinding? PreviewBinding =>
        PreviewArtifactDigest is null
            ? null
            : new ConfigurationImportPreviewBinding(
                Target,
                PreviewArtifactDigest,
                PreviewTargetRevisionDigest
                    ?? throw new InvalidOperationException(
                        "Preview target revision digest is missing."),
                PreviewSelectedSectionsDigest
                    ?? throw new InvalidOperationException(
                        "Preview selection digest is missing."),
                PreviewMappingDigest
                    ?? throw new InvalidOperationException(
                        "Preview mapping digest is missing."),
                PreviewApplyMode
                    ?? throw new InvalidOperationException(
                        "Preview apply mode is missing."),
                PreviewRequiredApprovalDigest
                    ?? throw new InvalidOperationException(
                        "Preview approval digest is missing."),
                PreviewExpiresAt
                    ?? throw new InvalidOperationException(
                        "Preview expiry is missing."));

    public static ConfigurationImportSession Create(
        Guid sessionId,
        ConfigurationImportTarget target,
        ConfigurationImportArtifactReference artifact,
        string accessTokenDigest,
        DateTime occurredAt,
        TimeSpan lifetime)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(artifact);
        ConfigurationImportContractGuard.ValidateDigest(
            accessTokenDigest,
            nameof(accessTokenDigest));
        ConfigurationImportContractGuard.RequireUtc(occurredAt, nameof(occurredAt));
        if (lifetime <= TimeSpan.Zero
            || lifetime > ConfigurationImportSessionLimits.MaximumSessionLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        DateTime expiresAt = occurredAt.Add(lifetime);
        if (artifact.ExpiresAt < expiresAt)
        {
            throw new ArgumentException(
                "Protected artifact expires before its import session.",
                nameof(artifact));
        }

        return new ConfigurationImportSession
        {
            SessionId = sessionId,
            TargetScope = target.Scope,
            TargetTenantId = target.TenantId,
            TargetAuthorityKey = target.AuthorityKey,
            ArtifactHandleId = artifact.Handle.Id,
            ArtifactDigest = artifact.Sha256Digest,
            ArtifactByteLength = artifact.ByteLength,
            ArtifactExpiresAt = artifact.ExpiresAt,
            AccessTokenDigest = accessTokenDigest,
            State = ConfigurationImportSessionState.Uploaded,
            CreatedAt = occurredAt,
            UpdatedAt = occurredAt,
            ExpiresAt = expiresAt,
            Revision = 1
        };
    }

    public bool MatchesTarget(ConfigurationImportTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return string.Equals(
            TargetAuthorityKey,
            target.AuthorityKey,
            StringComparison.Ordinal);
    }

    public void AuthorizePreview(
        ConfigurationImportTarget target,
        string presentedTokenDigest,
        DateTime occurredAt)
    {
        EnsureUsable(target, presentedTokenDigest, occurredAt);
    }

    public void MarkPreviewReady(
        ConfigurationImportPreviewBinding binding,
        DateTime occurredAt)
    {
        ArgumentNullException.ThrowIfNull(binding);
        EnsureActiveTime(occurredAt);
        if (!MatchesTarget(binding.Target)
            || !string.Equals(
                ArtifactDigest,
                binding.ArtifactDigest,
                StringComparison.Ordinal))
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.TargetMismatch);
        }

        PreviewArtifactDigest = binding.ArtifactDigest;
        PreviewTargetRevisionDigest = binding.TargetRevisionDigest;
        PreviewSelectedSectionsDigest = binding.SelectedSectionsDigest;
        PreviewMappingDigest = binding.MappingDigest;
        PreviewApplyMode = binding.ApplyMode;
        PreviewRequiredApprovalDigest = binding.RequiredApprovalDigest;
        PreviewExpiresAt = binding.ExpiresAt;
        State = ConfigurationImportSessionState.PreviewReady;
        UpdatedAt = occurredAt;
        Revision = checked(Revision + 1);
    }

    public void Consume(
        ConfigurationImportPreviewBinding binding,
        ConfigurationImportTarget target,
        string presentedTokenDigest,
        DateTime occurredAt)
    {
        ArgumentNullException.ThrowIfNull(binding);
        EnsureUsable(target, presentedTokenDigest, occurredAt);
        if (PreviewBinding is not { } persisted
            || !persisted.Matches(binding)
            || binding.ExpiresAt <= occurredAt)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.StalePreview);
        }

        State = ConfigurationImportSessionState.Consumed;
        ConsumedAt = occurredAt;
        UpdatedAt = occurredAt;
        Revision = checked(Revision + 1);
    }

    public void Cancel(DateTime occurredAt)
    {
        EnsureActiveTime(occurredAt);
        if (State == ConfigurationImportSessionState.Consumed)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.Replayed);
        }

        State = ConfigurationImportSessionState.Cancelled;
        CancelledAt = occurredAt;
        UpdatedAt = occurredAt;
        Revision = checked(Revision + 1);
    }

    public void Expire(DateTime occurredAt)
    {
        ConfigurationImportContractGuard.RequireUtc(occurredAt, nameof(occurredAt));
        if (occurredAt < ExpiresAt)
            throw new ArgumentOutOfRangeException(nameof(occurredAt));
        if (State is ConfigurationImportSessionState.Consumed
            or ConfigurationImportSessionState.Cancelled)
        {
            return;
        }

        State = ConfigurationImportSessionState.Expired;
        UpdatedAt = occurredAt;
        Revision = checked(Revision + 1);
    }

    public bool NeedsExpiry(DateTime occurredAt)
    {
        ConfigurationImportContractGuard.RequireUtc(occurredAt, nameof(occurredAt));
        return occurredAt >= ExpiresAt
            && State is ConfigurationImportSessionState.Uploaded
                or ConfigurationImportSessionState.PreviewReady;
    }

    private void EnsureUsable(
        ConfigurationImportTarget target,
        string presentedTokenDigest,
        DateTime occurredAt)
    {
        ArgumentNullException.ThrowIfNull(target);
        ConfigurationImportContractGuard.ValidateDigest(
            presentedTokenDigest,
            nameof(presentedTokenDigest));
        EnsureActiveTime(occurredAt);
        if (!MatchesTarget(target))
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.TargetMismatch);
        }

        if (!FixedTimeDigestEquals(AccessTokenDigest, presentedTokenDigest))
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.TokenInvalid);
        }
    }

    internal bool HasAccess(
        ConfigurationImportTarget target,
        string presentedTokenDigest) =>
        MatchesTarget(target)
        && FixedTimeDigestEquals(AccessTokenDigest, presentedTokenDigest);

    private void EnsureActiveTime(DateTime occurredAt)
    {
        ConfigurationImportContractGuard.RequireUtc(occurredAt, nameof(occurredAt));
        if (occurredAt >= ExpiresAt
            || State == ConfigurationImportSessionState.Expired)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.Expired);
        }

        if (State == ConfigurationImportSessionState.Cancelled)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.Cancelled);
        }

        if (State == ConfigurationImportSessionState.Consumed)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.Replayed);
        }
    }

    private static bool FixedTimeDigestEquals(string expected, string actual)
    {
        byte[] expectedBytes = Convert.FromHexString(expected);
        byte[] actualBytes = Convert.FromHexString(actual);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}

public sealed class ConfigurationImportSessionException(string failureCode)
    : InvalidOperationException("Configuration import session operation failed.")
{
    public string FailureCode { get; } = failureCode;
}

internal static class ConfigurationImportContractGuard
{
    public static void ValidateDigest(string digest, string parameterName)
    {
        if (digest.Length != 64
            || digest.Any(character =>
                !char.IsAsciiHexDigit(character)
                || char.IsAsciiLetterUpper(character)))
        {
            throw new ArgumentException(
                "Configuration import digest must be lowercase SHA-256.",
                parameterName);
        }
    }

    public static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("UTC timestamp required.", parameterName);
    }
}
