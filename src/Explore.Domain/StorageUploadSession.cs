using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class StorageUploadSession : ITenantEntity, IAuditableEntity, IConcurrencyAware
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public required string Provider { get; set; }
    public string RouteKey { get; set; } = StorageRouteKeys.General;
    public long PolicyMaxUploadBytes { get; set; }
    public string? PolicyVersion { get; set; }
    public long ExpectedSizeBytes { get; set; }
    public long ReservedBytes { get; set; }
    public required string ContentType { get; set; }
    public string? OriginalFileName { get; set; }
    public required string SafeDisplayName { get; set; }
    public string? Extension { get; set; }
    public required string Purpose { get; set; }
    public required string Visibility { get; set; }
    public string? OwningResourceKind { get; set; }
    public Guid? OwningResourceId { get; set; }
    public required string Status { get; set; }
    public string? ObjectKey { get; set; }
    public string? Sha256Checksum { get; set; }
    public Guid? StorageObjectId { get; set; }
    public StorageObject? StorageObject { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UploadStartedAt { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public DateTime? FailedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    public void ReserveObjectKey(string objectKey)
    {
        if (Status != StorageUploadSessionStates.Reserved)
        {
            throw new InvalidOperationException("Only reserved upload sessions can reserve an object key.");
        }

        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new ArgumentException("A storage object key is required.", nameof(objectKey));
        }

        ObjectKey = objectKey;
    }

    public void MarkUploading(DateTime utcNow)
    {
        if (Status != StorageUploadSessionStates.Reserved)
        {
            throw new InvalidOperationException("Only reserved upload sessions can start uploading.");
        }

        Status = StorageUploadSessionStates.Uploading;
        UploadStartedAt = utcNow;
    }

    public void Finalize(Guid storageObjectId, string objectKey, string? sha256Checksum, DateTime utcNow)
    {
        if (Status is not StorageUploadSessionStates.Reserved and not StorageUploadSessionStates.Uploading)
        {
            throw new InvalidOperationException("Only reserved or uploading sessions can be finalized.");
        }

        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new ArgumentException("Finalized storage sessions require a provider object key.", nameof(objectKey));
        }

        StorageObjectId = storageObjectId;
        ObjectKey = objectKey;
        Sha256Checksum = sha256Checksum;
        Status = StorageUploadSessionStates.Finalized;
        FinalizedAt = utcNow;
    }

    public void Cancel(DateTime utcNow)
    {
        if (Status == StorageUploadSessionStates.Finalized)
        {
            throw new InvalidOperationException("Finalized upload sessions cannot be canceled.");
        }

        Status = StorageUploadSessionStates.Canceled;
        CanceledAt = utcNow;
    }

    public void MarkExpired(DateTime utcNow)
    {
        if (Status == StorageUploadSessionStates.Finalized)
        {
            return;
        }

        Status = StorageUploadSessionStates.Expired;
        FailedAt = utcNow;
        FailureCode = "upload_session_expired";
    }

    public void Fail(string failureCode, string? failureMessage, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(failureCode))
        {
            throw new ArgumentException("A storage upload failure code is required.", nameof(failureCode));
        }

        Status = StorageUploadSessionStates.Failed;
        FailureCode = failureCode.Trim();
        FailureMessage = string.IsNullOrWhiteSpace(failureMessage) ? null : failureMessage.Trim();
        FailedAt = utcNow;
    }
}
