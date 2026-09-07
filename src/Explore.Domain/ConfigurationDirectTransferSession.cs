namespace Explore.Domain;

public enum ConfigurationDirectTransferStatus
{
    AwaitingApprovals = 1,
    Ready = 2,
    Receiving = 3,
    Received = 4,
    Cancelled = 5,
    Expired = 6,
    Promoting = 7,
    Promoted = 8
}

public sealed class ConfigurationDirectTransferSession
{
    public const int MaximumAuthorityLength = 200;

    private ConfigurationDirectTransferSession()
    {
    }

    public Guid Id { get; private set; }
    public string SourceAuthority { get; private set; } = string.Empty;
    public string TargetAuthorityKey { get; private set; } = string.Empty;
    public Guid? TargetTenantId { get; private set; }
    public string DestinationOriginDigest { get; private set; } = string.Empty;
    public string DestinationProofDigest { get; private set; } = string.Empty;
    public string NonceDigest { get; private set; } = string.Empty;
    public string ArtifactDigest { get; private set; } = string.Empty;
    public int ArtifactByteLength { get; private set; }
    public int NextOffset { get; private set; }
    public int LastChunkOffset { get; private set; } = -1;
    public int LastChunkByteLength { get; private set; }
    public string? LastChunkDigest { get; private set; }
    public Guid? SourceApprovedBy { get; private set; }
    public Guid? DestinationApprovedBy { get; private set; }
    public ConfigurationDirectTransferStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public static ConfigurationDirectTransferSession Create(
        Guid id,
        string sourceAuthority,
        string targetAuthorityKey,
        Guid? targetTenantId,
        string destinationOriginDigest,
        string destinationProofDigest,
        string nonceDigest,
        string artifactDigest,
        int artifactByteLength,
        DateTime createdAt,
        DateTime expiresAt)
    {
        RequireVersion7(id, nameof(id));
        RequireDigest(destinationOriginDigest, nameof(destinationOriginDigest));
        RequireDigest(destinationProofDigest, nameof(destinationProofDigest));
        RequireDigest(nonceDigest, nameof(nonceDigest));
        RequireDigest(artifactDigest, nameof(artifactDigest));
        RequireUtc(createdAt, nameof(createdAt));
        RequireUtc(expiresAt, nameof(expiresAt));
        if (artifactByteLength is < 1 or > 4 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(artifactByteLength));
        if (expiresAt <= createdAt || expiresAt > createdAt.AddHours(1))
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAuthority);
        string authority = sourceAuthority.Trim();
        if (authority.Length > MaximumAuthorityLength)
            throw new ArgumentOutOfRangeException(nameof(sourceAuthority));
        ArgumentException.ThrowIfNullOrWhiteSpace(targetAuthorityKey);
        string targetAuthority = targetAuthorityKey.Trim();
        if (targetAuthority.Length > MaximumAuthorityLength
            || targetTenantId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(targetAuthorityKey));
        }
        if ((targetTenantId is null) != string.Equals(
                targetAuthority,
                "instance",
                StringComparison.Ordinal))
        {
            throw new ArgumentException("Transfer target authority is inconsistent.");
        }

        return new ConfigurationDirectTransferSession
        {
            Id = id,
            SourceAuthority = authority,
            TargetAuthorityKey = targetAuthority,
            TargetTenantId = targetTenantId,
            DestinationOriginDigest = destinationOriginDigest,
            DestinationProofDigest = destinationProofDigest,
            NonceDigest = nonceDigest,
            ArtifactDigest = artifactDigest,
            ArtifactByteLength = artifactByteLength,
            Status = ConfigurationDirectTransferStatus.AwaitingApprovals,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt
        };
    }

    public void ApproveSource(Guid actorUserId, DateTime occurredAt)
    {
        EnsureActive(occurredAt);
        RequireActor(actorUserId);
        if (DestinationApprovedBy == actorUserId)
            throw new InvalidOperationException(
                "Source and destination approvals require different actors.");
        SourceApprovedBy ??= actorUserId;
        RefreshReadyState();
    }

    public void ApproveDestination(
        Guid actorUserId,
        string destinationProofDigest,
        DateTime occurredAt)
    {
        EnsureActive(occurredAt);
        RequireActor(actorUserId);
        if (!string.Equals(
                DestinationProofDigest,
                destinationProofDigest,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Destination proof does not match the transfer session.");
        }
        DestinationApprovedBy ??= actorUserId;
        RefreshReadyState();
    }

    public bool AcceptChunk(
        int offset,
        int byteLength,
        string chunkDigest,
        string nonceDigest,
        DateTime occurredAt)
    {
        EnsureActive(occurredAt);
        RequireDigest(chunkDigest, nameof(chunkDigest));
        if (!string.Equals(NonceDigest, nonceDigest, StringComparison.Ordinal))
            throw new InvalidOperationException("Transfer nonce is invalid.");
        if (Status is not (ConfigurationDirectTransferStatus.Ready
            or ConfigurationDirectTransferStatus.Receiving))
        {
            throw new InvalidOperationException(
                "Both transfer parties must approve before data is accepted.");
        }
        if (offset == LastChunkOffset
            && byteLength == LastChunkByteLength
            && string.Equals(
                chunkDigest,
                LastChunkDigest,
                StringComparison.Ordinal))
        {
            return false;
        }
        if (offset != NextOffset
            || byteLength < 1
            || offset + byteLength > ArtifactByteLength)
        {
            throw new InvalidOperationException(
                "Transfer chunk is not the next bounded range.");
        }

        LastChunkOffset = offset;
        LastChunkByteLength = byteLength;
        LastChunkDigest = chunkDigest;
        NextOffset += byteLength;
        Status = ConfigurationDirectTransferStatus.Receiving;
        return true;
    }

    public void Complete(
        string artifactDigest,
        string nonceDigest,
        DateTime occurredAt)
    {
        RequireUtc(occurredAt, nameof(occurredAt));
        RequireDigest(artifactDigest, nameof(artifactDigest));
        RequireDigest(nonceDigest, nameof(nonceDigest));
        if (!string.Equals(NonceDigest, nonceDigest, StringComparison.Ordinal)
            || !string.Equals(ArtifactDigest, artifactDigest, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Transferred artifact does not match its binding.");
        }
        if (Status == ConfigurationDirectTransferStatus.Received)
            return;
        EnsureActive(occurredAt);
        if (NextOffset != ArtifactByteLength)
            throw new InvalidOperationException("Transferred artifact is incomplete.");
        Status = ConfigurationDirectTransferStatus.Received;
        CompletedAt = occurredAt;
    }

    public void Cancel(DateTime occurredAt)
    {
        RequireUtc(occurredAt, nameof(occurredAt));
        if (Status is ConfigurationDirectTransferStatus.Received
            or ConfigurationDirectTransferStatus.Promoting
            or ConfigurationDirectTransferStatus.Promoted)
            throw new InvalidOperationException("A received transfer cannot be cancelled.");
        if (Status == ConfigurationDirectTransferStatus.Cancelled)
            return;
        Status = occurredAt >= ExpiresAt
            ? ConfigurationDirectTransferStatus.Expired
            : ConfigurationDirectTransferStatus.Cancelled;
        CompletedAt = occurredAt;
    }

    private void RefreshReadyState()
    {
        if (SourceApprovedBy.HasValue && DestinationApprovedBy.HasValue)
            Status = ConfigurationDirectTransferStatus.Ready;
    }

    private void EnsureActive(DateTime occurredAt)
    {
        RequireUtc(occurredAt, nameof(occurredAt));
        if (occurredAt >= ExpiresAt)
        {
            Status = ConfigurationDirectTransferStatus.Expired;
            CompletedAt ??= occurredAt;
            throw new InvalidOperationException("Transfer session expired.");
        }
        if (Status is ConfigurationDirectTransferStatus.Cancelled
            or ConfigurationDirectTransferStatus.Expired
            or ConfigurationDirectTransferStatus.Received
            or ConfigurationDirectTransferStatus.Promoting
            or ConfigurationDirectTransferStatus.Promoted)
        {
            throw new InvalidOperationException("Transfer session is terminal.");
        }
    }

    private static void RequireDigest(string value, string parameterName)
    {
        if (value.Length != 64 || value.Any(character =>
                character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "Lowercase SHA-256 digest required.",
                parameterName);
        }
    }

    private static void RequireVersion7(Guid value, string parameterName)
    {
        if (value == Guid.Empty || value.Version != 7)
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private static void RequireActor(Guid actorUserId)
    {
        if (actorUserId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(actorUserId));
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("UTC timestamp required.", parameterName);
    }
}
