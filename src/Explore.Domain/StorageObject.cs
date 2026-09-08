// ABOUTME: Tenant-scoped metadata record for stored files addressed by application IDs, not raw provider keys.
// ABOUTME: Models provider, visibility, lifecycle, safe display name, and quarantine/delete state for local-first storage.

using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class StorageObject : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public Guid Id { get; set; }

    public int FileTypeId { get; set; }
    public required FileType FileType { get; set; }

    public required string Uri { get; set; }
    public string? ObjectKey { get; set; }
    public required string Provider { get; set; }
    public required string FullName { get; set; }
    public required string SafeDisplayName { get; set; }
    public required string Extension { get; set; }
    public string? ContentType { get; set; }
    public string? Sha256Checksum { get; set; }
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
