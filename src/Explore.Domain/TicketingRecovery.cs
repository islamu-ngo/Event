using Explore.Domain.Interfaces;

namespace Explore.Domain;

public enum TicketingRecoveryStatus
{
    RecoveryOnly = 1,
    Validated = 2,
    AuthorityRotated = 3,
    WorkersOpen = 4,
    SalesOpen = 5,
    Failed = 6,
}

public enum TicketingRecoveryValidationOutcome
{
    Validated = 1,
    ReleaseMismatch = 2,
    SchemaMismatch = 3,
    MissingKey = 4,
    StaleAuthority = 5,
    StaleProviderCursor = 6,
    MissingIdempotency = 7,
    StaleWorkerFence = 8,
}

public enum TicketingRecoveryReissueStatus
{
    Pending = 1,
    Completed = 2,
    Failed = 3,
}

public sealed record TicketingRecoveryManifest
{
    private TicketingRecoveryManifest(
        Guid operationId,
        Guid tenantId,
        string releaseRevision,
        string schemaRevision,
        long databaseCheckpoint,
        DateTime objectCutoffUtc,
        int retainedKeyVersion,
        long authorityFloor,
        long providerCursor,
        long idempotencyFloor,
        long workerFence,
        int capabilityGeneration,
        int credentialGeneration,
        string digest)
    {
        OperationId = operationId;
        TenantId = tenantId;
        ReleaseRevision = releaseRevision;
        SchemaRevision = schemaRevision;
        DatabaseCheckpoint = databaseCheckpoint;
        ObjectCutoffUtc = objectCutoffUtc;
        RetainedKeyVersion = retainedKeyVersion;
        AuthorityFloor = authorityFloor;
        ProviderCursor = providerCursor;
        IdempotencyFloor = idempotencyFloor;
        WorkerFence = workerFence;
        CapabilityGeneration = capabilityGeneration;
        CredentialGeneration = credentialGeneration;
        Digest = digest;
    }

    public Guid OperationId { get; }
    public Guid TenantId { get; }
    public string ReleaseRevision { get; }
    public string SchemaRevision { get; }
    public long DatabaseCheckpoint { get; }
    public DateTime ObjectCutoffUtc { get; }
    public int RetainedKeyVersion { get; }
    public long AuthorityFloor { get; }
    public long ProviderCursor { get; }
    public long IdempotencyFloor { get; }
    public long WorkerFence { get; }
    public int CapabilityGeneration { get; }
    public int CredentialGeneration { get; }
    public string Digest { get; }

    public static TicketingRecoveryManifest Create(
        Guid operationId,
        Guid tenantId,
        string releaseRevision,
        string schemaRevision,
        long databaseCheckpoint,
        DateTime objectCutoffUtc,
        int retainedKeyVersion,
        long authorityFloor,
        long providerCursor,
        long idempotencyFloor,
        long workerFence,
        int capabilityGeneration,
        int credentialGeneration,
        string digest)
    {
        RequireUuidV7(operationId, nameof(operationId));
        RequireUuidV7(tenantId, nameof(tenantId));
        string release = RequireRevision(releaseRevision, nameof(releaseRevision));
        string schema = RequireRevision(schemaRevision, nameof(schemaRevision));
        DateTime cutoff = RequireUtc(objectCutoffUtc, nameof(objectCutoffUtc));
        ArgumentOutOfRangeException.ThrowIfNegative(databaseCheckpoint);
        ArgumentOutOfRangeException.ThrowIfNegative(authorityFloor);
        ArgumentOutOfRangeException.ThrowIfNegative(providerCursor);
        ArgumentOutOfRangeException.ThrowIfNegative(idempotencyFloor);
        ArgumentOutOfRangeException.ThrowIfNegative(workerFence);
        ArgumentOutOfRangeException.ThrowIfNegative(capabilityGeneration);
        ArgumentOutOfRangeException.ThrowIfNegative(credentialGeneration);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retainedKeyVersion);
        string normalizedDigest = digest?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedDigest.Length != 64 ||
            normalizedDigest.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "Recovery manifest digest must be a lowercase SHA-256 value.",
                nameof(digest));
        }

        return new TicketingRecoveryManifest(
            operationId,
            tenantId,
            release,
            schema,
            databaseCheckpoint,
            cutoff,
            retainedKeyVersion,
            authorityFloor,
            providerCursor,
            idempotencyFloor,
            workerFence,
            capabilityGeneration,
            credentialGeneration,
            normalizedDigest);
    }

    internal static DateTime RequireUtc(DateTime value, string parameterName) =>
        value != default && value.Kind == DateTimeKind.Utc
            ? value
            : throw new ArgumentException(
                "Recovery timestamps must be non-default UTC values.",
                parameterName);

    internal static void RequireUuidV7(Guid value, string parameterName)
    {
        if (value == Guid.Empty || value.Version != 7 || value.Variant is < 8 or > 11)
        {
            throw new ArgumentException(
                "Recovery identities must be UUIDv7.",
                parameterName);
        }
    }

    private static string RequireRevision(string value, string parameterName)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is >= 1 and <= 100
            ? normalized
            : throw new ArgumentException(
                "Recovery revisions must contain 1-100 characters.",
                parameterName);
    }
}

public sealed class TicketingRecoveryCheckpoint :
    ITenantEntity,
    IAuditableEntity,
    IConcurrencyAware
{
    private Guid _tenantId;

    private TicketingRecoveryCheckpoint()
    {
    }

    private TicketingRecoveryCheckpoint(
        TicketingRecoveryManifest manifest,
        DateTime createdAtUtc)
    {
        Id = Guid.CreateVersion7();
        TenantId = manifest.TenantId;
        RecoveryOperationId = manifest.OperationId;
        ManifestDigest = manifest.Digest;
        ReleaseRevision = manifest.ReleaseRevision;
        SchemaRevision = manifest.SchemaRevision;
        DatabaseCheckpoint = manifest.DatabaseCheckpoint;
        ObjectCutoffUtc = manifest.ObjectCutoffUtc;
        RetainedKeyVersion = manifest.RetainedKeyVersion;
        AuthorityFloor = manifest.AuthorityFloor;
        ProviderCursor = manifest.ProviderCursor;
        IdempotencyFloor = manifest.IdempotencyFloor;
        WorkerFence = manifest.WorkerFence;
        CapabilityGeneration = manifest.CapabilityGeneration;
        CredentialGeneration = manifest.CredentialGeneration;
        Status = TicketingRecoveryStatus.RecoveryOnly;
        ConcurrencyStamp = Guid.CreateVersion7();
        CreatedAt = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(
            ref _tenantId,
            value,
            nameof(TicketingRecoveryCheckpoint));
    }
    public Guid RecoveryOperationId { get; private set; }
    public string ManifestDigest { get; private set; } = string.Empty;
    public string ReleaseRevision { get; private set; } = string.Empty;
    public string SchemaRevision { get; private set; } = string.Empty;
    public long DatabaseCheckpoint { get; private set; }
    public DateTime ObjectCutoffUtc { get; private set; }
    public int RetainedKeyVersion { get; private set; }
    public long AuthorityFloor { get; private set; }
    public long ProviderCursor { get; private set; }
    public long IdempotencyFloor { get; private set; }
    public long WorkerFence { get; private set; }
    public int CapabilityGeneration { get; private set; }
    public int CredentialGeneration { get; private set; }
    public TicketingRecoveryStatus Status { get; private set; }
    public DateTime? ValidatedAt { get; private set; }
    public DateTime? AuthorityRotatedAt { get; private set; }
    public DateTime? WorkersOpenedAt { get; private set; }
    public DateTime? SalesOpenedAt { get; private set; }
    public string? FailureCode { get; private set; }
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static TicketingRecoveryCheckpoint Begin(
        TicketingRecoveryManifest manifest,
        DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return new TicketingRecoveryCheckpoint(
            manifest,
            TicketingRecoveryManifest.RequireUtc(
                createdAtUtc,
                nameof(createdAtUtc)));
    }

    public TicketingRecoveryValidationOutcome Validate(
        string runningReleaseRevision,
        string runningSchemaRevision,
        int minimumRetainedKeyVersion,
        long minimumAuthorityFloor,
        long minimumProviderCursor,
        long minimumIdempotencyFloor,
        long minimumWorkerFence,
        DateTime validatedAtUtc)
    {
        DateTime validatedAt = TicketingRecoveryManifest.RequireUtc(
            validatedAtUtc,
            nameof(validatedAtUtc));
        if (Status != TicketingRecoveryStatus.RecoveryOnly)
        {
            return Status is
                TicketingRecoveryStatus.Validated or
                TicketingRecoveryStatus.AuthorityRotated or
                TicketingRecoveryStatus.WorkersOpen or
                TicketingRecoveryStatus.SalesOpen
                ? TicketingRecoveryValidationOutcome.Validated
                : TicketingRecoveryValidationOutcome.StaleAuthority;
        }

        TicketingRecoveryValidationOutcome outcome =
            !string.Equals(
                ReleaseRevision,
                runningReleaseRevision?.Trim(),
                StringComparison.Ordinal)
                ? TicketingRecoveryValidationOutcome.ReleaseMismatch
                : !string.Equals(
                    SchemaRevision,
                    runningSchemaRevision?.Trim(),
                    StringComparison.Ordinal)
                    ? TicketingRecoveryValidationOutcome.SchemaMismatch
                    : RetainedKeyVersion < minimumRetainedKeyVersion
                        ? TicketingRecoveryValidationOutcome.MissingKey
                        : AuthorityFloor < minimumAuthorityFloor
                            ? TicketingRecoveryValidationOutcome.StaleAuthority
                            : ProviderCursor < minimumProviderCursor
                                ? TicketingRecoveryValidationOutcome.StaleProviderCursor
                                : IdempotencyFloor < minimumIdempotencyFloor
                                    ? TicketingRecoveryValidationOutcome.MissingIdempotency
                                    : WorkerFence < minimumWorkerFence
                                        ? TicketingRecoveryValidationOutcome.StaleWorkerFence
                                        : TicketingRecoveryValidationOutcome.Validated;
        if (outcome == TicketingRecoveryValidationOutcome.Validated)
        {
            Status = TicketingRecoveryStatus.Validated;
            ValidatedAt = validatedAt;
            Touch(validatedAt);
        }

        return outcome;
    }

    public bool TryRotateBearerAuthority(
        int capabilityGeneration,
        int credentialGeneration,
        long workerFence,
        DateTime rotatedAtUtc)
    {
        DateTime rotatedAt = TicketingRecoveryManifest.RequireUtc(
            rotatedAtUtc,
            nameof(rotatedAtUtc));
        if (Status != TicketingRecoveryStatus.Validated ||
            capabilityGeneration <= CapabilityGeneration ||
            credentialGeneration <= CredentialGeneration ||
            workerFence <= WorkerFence)
        {
            return false;
        }

        CapabilityGeneration = capabilityGeneration;
        CredentialGeneration = credentialGeneration;
        WorkerFence = workerFence;
        Status = TicketingRecoveryStatus.AuthorityRotated;
        AuthorityRotatedAt = rotatedAt;
        Touch(rotatedAt);
        return true;
    }

    public bool TryOpenWorkers(long expectedWorkerFence, DateTime openedAtUtc)
    {
        DateTime openedAt = TicketingRecoveryManifest.RequireUtc(
            openedAtUtc,
            nameof(openedAtUtc));
        if (Status != TicketingRecoveryStatus.AuthorityRotated ||
            expectedWorkerFence != WorkerFence)
        {
            return false;
        }

        Status = TicketingRecoveryStatus.WorkersOpen;
        WorkersOpenedAt = openedAt;
        Touch(openedAt);
        return true;
    }

    public bool TryOpenSales(DateTime openedAtUtc)
    {
        DateTime openedAt = TicketingRecoveryManifest.RequireUtc(
            openedAtUtc,
            nameof(openedAtUtc));
        if (Status != TicketingRecoveryStatus.WorkersOpen)
        {
            return false;
        }

        Status = TicketingRecoveryStatus.SalesOpen;
        SalesOpenedAt = openedAt;
        Touch(openedAt);
        return true;
    }

    public bool StopSales(
        long nextWorkerFence,
        DateTime occurredAtUtc)
    {
        DateTime occurredAt = TicketingRecoveryManifest.RequireUtc(
            occurredAtUtc,
            nameof(occurredAtUtc));
        if (Status == TicketingRecoveryStatus.Failed ||
            nextWorkerFence <= WorkerFence)
        {
            return false;
        }

        WorkerFence = nextWorkerFence;
        Status = TicketingRecoveryStatus.RecoveryOnly;
        ValidatedAt = null;
        AuthorityRotatedAt = null;
        WorkersOpenedAt = null;
        SalesOpenedAt = null;
        Touch(occurredAt);
        return true;
    }

    public bool PauseWorkers(DateTime occurredAtUtc)
    {
        DateTime occurredAt = TicketingRecoveryManifest.RequireUtc(
            occurredAtUtc,
            nameof(occurredAtUtc));
        if (Status is not (
            TicketingRecoveryStatus.WorkersOpen or
            TicketingRecoveryStatus.SalesOpen))
        {
            return false;
        }

        Status = TicketingRecoveryStatus.AuthorityRotated;
        WorkersOpenedAt = null;
        SalesOpenedAt = null;
        Touch(occurredAt);
        return true;
    }

    public void Fail(string failureCode, DateTime failedAtUtc)
    {
        string normalized = failureCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length is < 1 or > 64)
        {
            throw new ArgumentException(
                "Recovery failure code must contain 1-64 characters.",
                nameof(failureCode));
        }

        DateTime failedAt = TicketingRecoveryManifest.RequireUtc(
            failedAtUtc,
            nameof(failedAtUtc));
        Status = TicketingRecoveryStatus.Failed;
        FailureCode = normalized;
        Touch(failedAt);
    }

    private void Touch(DateTime timestamp)
    {
        UpdatedAt = timestamp;
        ConcurrencyStamp = Guid.CreateVersion7();
    }
}

public sealed class TicketingRecoveryReissueIntent :
    ITenantEntity,
    IAuditableEntity,
    IConcurrencyAware
{
    private Guid _tenantId;

    private TicketingRecoveryReissueIntent()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(
            ref _tenantId,
            value,
            nameof(TicketingRecoveryReissueIntent));
    }
    public Guid RecoveryOperationId { get; private set; }
    public Guid AdmissionTicketId { get; private set; }
    public int RequiredCredentialGeneration { get; private set; }
    public TicketingRecoveryReissueStatus Status { get; private set; }
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static TicketingRecoveryReissueIntent Create(
        Guid tenantId,
        Guid recoveryOperationId,
        Guid admissionTicketId,
        int requiredCredentialGeneration,
        DateTime createdAtUtc)
    {
        TicketingRecoveryManifest.RequireUuidV7(tenantId, nameof(tenantId));
        TicketingRecoveryManifest.RequireUuidV7(
            recoveryOperationId,
            nameof(recoveryOperationId));
        TicketingRecoveryManifest.RequireUuidV7(
            admissionTicketId,
            nameof(admissionTicketId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            requiredCredentialGeneration);
        DateTime createdAt = TicketingRecoveryManifest.RequireUtc(
            createdAtUtc,
            nameof(createdAtUtc));
        return new TicketingRecoveryReissueIntent
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            RecoveryOperationId = recoveryOperationId,
            AdmissionTicketId = admissionTicketId,
            RequiredCredentialGeneration = requiredCredentialGeneration,
            Status = TicketingRecoveryReissueStatus.Pending,
            ConcurrencyStamp = Guid.CreateVersion7(),
            CreatedAt = createdAt,
        };
    }
}
