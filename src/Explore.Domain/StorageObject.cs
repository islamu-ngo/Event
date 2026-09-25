using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class StorageObject : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public Guid Id { get; set; }

    public int FileTypeId { get; set; }
    public required FileType FileType { get; set; }

    public required string Uri { get; set; }
    public string? ObjectKey { get; set; }
    public Guid? StorageProviderBindingId { get; set; }
    public string? ProviderVersionId { get; set; }
    public required string Provider { get; set; }
    public required string FullName { get; set; }
    public required string SafeDisplayName { get; set; }
    public required string Extension { get; set; }
    public string? ContentType { get; set; }
    public string? Sha256Checksum { get; set; }
    public string DocumentSafetyState { get; private set; } = StorageDocumentSafetyStates.Unavailable;
    public Guid? InspectedObjectId { get; private set; }
    public string? InspectedSha256Checksum { get; private set; }
    public bool HasBoundDocumentInspection => DocumentSafetyState == StorageDocumentSafetyStates.Unscanned
        && InspectedObjectId == Id && InspectedSha256Checksum is not null
        && string.Equals(InspectedSha256Checksum, Sha256Checksum, StringComparison.Ordinal);
    public long Size { get; set; }
    public required string Visibility { get; set; }
    public required string Purpose { get; set; }
    public required string LifecycleState { get; set; }
    public string? OwningResourceKind { get; set; }
    public Guid? OwningResourceId { get; set; }
    /// <summary>Operational registration-content expiry, not authority to delete legally held bytes.</summary>
    public DateTime? RegistrationContentRetentionUntilUtc { get; set; }
    public DateTime? QuarantinedAt { get; set; }
    public Guid? QuarantinedBy { get; set; }
    public string? QuarantineReason { get; set; }

    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }

    public Guid? ActorId { get; set; }
    public Actor? Actor { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    /// <summary>Records document validation only; no scanner verdict or Clean transition exists.</summary>
    public void RecordEventResourceInspection(Guid objectId, string sha256Checksum)
    {
        if (DocumentSafetyState != StorageDocumentSafetyStates.Unavailable || objectId != Id || Id == Guid.Empty
            || Purpose != StorageObjectPurposes.EventResource || Visibility != StorageObjectVisibilities.PrivateOwner
            || OwningResourceKind != StorageOwningResourceKinds.EventResource || OwningResourceId is null
            || OwningResourceId == Guid.Empty || sha256Checksum.Length != 64
            || !sha256Checksum.All(char.IsAsciiHexDigit)
            || !string.Equals(Sha256Checksum, sha256Checksum, StringComparison.Ordinal))
            throw new InvalidOperationException("Inspection must bind the reserved resource object's exact content identity once.");
        InspectedObjectId = objectId;
        InspectedSha256Checksum = sha256Checksum;
        DocumentSafetyState = StorageDocumentSafetyStates.Unscanned;
    }

    public void MarkQuarantined(Guid? userId, string reason, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A quarantine reason is required.", nameof(reason));
        }

        LifecycleState = StorageObjectLifecycleStates.Quarantined;
        QuarantinedAt = utcNow;
        QuarantinedBy = userId;
        QuarantineReason = reason.Trim();
    }

    public void RequestDelete()
    {
        if (LifecycleState == StorageObjectLifecycleStates.Deleted)
        {
            return;
        }

        LifecycleState = StorageObjectLifecycleStates.DeleteRequested;
    }

    public void MarkDeleted(Guid? userId, DateTime utcNow)
    {
        if (LifecycleState == StorageObjectLifecycleStates.Deleted && IsDeleted)
        {
            return;
        }

        LifecycleState = StorageObjectLifecycleStates.Deleted;
        IsDeleted = true;
        DeletedAt = utcNow;
        DeletedBy = userId;
    }
}
