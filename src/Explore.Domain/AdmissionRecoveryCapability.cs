using Explore.Domain.Interfaces;

namespace Explore.Domain;

public enum AdmissionRecoveryTransitionOutcome
{
    Consumed,
    AlreadyConsumed,
    Expired,
    Rotated
}

public sealed class AdmissionRecoveryCapability :
    ITenantEntity,
    IAuditableEntity,
    IConcurrencyAware
{
    private Guid tenantId;

    private AdmissionRecoveryCapability()
    {
    }

    private AdmissionRecoveryCapability(
        Guid id,
        Guid tenantId,
        Guid recoveryRequestId,
        Guid admissionTicketId,
        string purpose,
        int capabilityVersion,
        int lookupKeyVersion,
        string lookupDigest,
        DateTime expiresAt,
        DateTime createdAt,
        string? locatorDigest)
    {
        Id = RequireUuidV7(id, nameof(id));
        TenantId = RequireUuidV7(tenantId, nameof(tenantId));
        RecoveryRequestId = RequireUuidV7(recoveryRequestId, nameof(recoveryRequestId));
        AdmissionTicketId = RequireUuidV7(admissionTicketId, nameof(admissionTicketId));
        Purpose = string.IsNullOrWhiteSpace(purpose)
            ? throw new ArgumentException("Recovery purpose is required.", nameof(purpose))
            : purpose.Trim();
        if (capabilityVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capabilityVersion));
        }

        if (lookupKeyVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lookupKeyVersion));
        }

        if (string.IsNullOrWhiteSpace(lookupDigest))
        {
            throw new ArgumentException("Recovery lookup digest is required.", nameof(lookupDigest));
        }
        if (string.IsNullOrWhiteSpace(locatorDigest))
        {
            throw new ArgumentException("Recovery locator digest is required.", nameof(locatorDigest));
        }

        CreatedAt = RequireUtc(createdAt, nameof(createdAt));
        ExpiresAt = RequireUtc(expiresAt, nameof(expiresAt));
        if (ExpiresAt <= CreatedAt)
        {
            throw new ArgumentException("Recovery expiry must follow creation.", nameof(expiresAt));
        }

        CapabilityVersion = capabilityVersion;
        LookupKeyVersion = lookupKeyVersion;
        LookupDigest = lookupDigest;
        LocatorDigest = locatorDigest;
        ActiveUniquenessSlot = 0;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public Guid Id { get; private set; }

    public Guid TenantId
    {
        get => tenantId;
        private set => TenantIdentity.Set(ref tenantId, value, nameof(AdmissionRecoveryCapability));
    }

    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => TenantIdentity.Set(ref tenantId, value, nameof(AdmissionRecoveryCapability));
    }

    public Guid RecoveryRequestId { get; private set; }
    public Guid AdmissionTicketId { get; private set; }
    public string Purpose { get; private set; } = string.Empty;
    public int CapabilityVersion { get; private set; }
    public int LookupKeyVersion { get; private set; }
    public string LookupDigest { get; private set; } = string.Empty;
    public string LocatorDigest { get; private set; } = string.Empty;
    public DateTime ExpiresAt { get; private set; }
    public DateTime? ConsumedAt { get; private set; }
    public DateTime? RotatedAt { get; private set; }
    public int ActiveUniquenessSlot { get; private set; }
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static AdmissionRecoveryCapability Create(
        Guid id,
        Guid tenantId,
        Guid recoveryRequestId,
        Guid admissionTicketId,
        string purpose,
        int capabilityVersion,
        int lookupKeyVersion,
        string lookupDigest,
        DateTime expiresAt,
        DateTime createdAt,
        string? locatorDigest = null) =>
        new(
            id,
            tenantId,
            recoveryRequestId,
            admissionTicketId,
            purpose,
            capabilityVersion,
            lookupKeyVersion,
            lookupDigest,
            expiresAt,
            createdAt,
            locatorDigest ?? lookupDigest);

    public AdmissionRecoveryTransitionOutcome TryConsume(DateTime consumedAt)
    {
        DateTime utc = RequireUtc(consumedAt, nameof(consumedAt));
        if (RotatedAt.HasValue)
        {
            return AdmissionRecoveryTransitionOutcome.Rotated;
        }

        if (ConsumedAt.HasValue)
        {
            return AdmissionRecoveryTransitionOutcome.AlreadyConsumed;
        }

        if (utc >= ExpiresAt)
        {
            return AdmissionRecoveryTransitionOutcome.Expired;
        }

        ConsumedAt = utc;
        ActiveUniquenessSlot = CapabilityVersion;
        UpdatedAt = utc;
        return AdmissionRecoveryTransitionOutcome.Consumed;
    }

    public AdmissionRecoveryTransitionOutcome TryRotate(DateTime rotatedAt)
    {
        DateTime utc = RequireUtc(rotatedAt, nameof(rotatedAt));
        if (ConsumedAt.HasValue)
        {
            return AdmissionRecoveryTransitionOutcome.AlreadyConsumed;
        }

        if (RotatedAt.HasValue)
        {
            return AdmissionRecoveryTransitionOutcome.Rotated;
        }

        RotatedAt = utc;
        ActiveUniquenessSlot = CapabilityVersion;
        UpdatedAt = utc;
        return AdmissionRecoveryTransitionOutcome.Rotated;
    }

    public override string ToString() =>
        $"AdmissionRecoveryCapability({Id}, v{CapabilityVersion}, <redacted>)";

    private static Guid RequireUuidV7(Guid value, string parameterName)
    {
        if (value == Guid.Empty || value.Version != 7 || value.Variant is < 8 or > 11)
        {
            throw new ArgumentException("Recovery lineage identity must be UUIDv7.", parameterName);
        }

        return value;
    }

    private static DateTime RequireUtc(DateTime value, string parameterName)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Recovery timestamp must be a non-default UTC value.", parameterName);
        }

        return value;
    }
}
