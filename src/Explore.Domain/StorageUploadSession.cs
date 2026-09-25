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
    public Guid? ExpectedResourceVersion { get; private set; }
    public Guid? FinalizedResourceVersion { get; private set; }
    public required string Status { get; set; }
    public string? ObjectKey { get; set; }
    public Guid? StorageProviderBindingId { get; set; }
    public string? ProviderVersionId { get; set; }
    public bool ProducerSettled { get; private set; }
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

    public void BindEventResourceVersion(Guid expectedVersion)
    {
        if (ExpectedResourceVersion.HasValue || expectedVersion == Guid.Empty
            || Status != StorageUploadSessionStates.Reserved
            || Purpose != StorageObjectPurposes.EventResource || Visibility != StorageObjectVisibilities.PrivateOwner
            || OwningResourceKind != StorageOwningResourceKinds.EventResource || OwningResourceId is null
            || OwningResourceId == Guid.Empty || UserId is null || UserId == Guid.Empty)
            throw new InvalidOperationException("Resource reservations require immutable resource, subject and version binding.");
        ExpectedResourceVersion = expectedVersion;
    }

    public void StageEventResourceObject(Guid objectId)
    {
        if (!ExpectedResourceVersion.HasValue || Status != StorageUploadSessionStates.Uploading
            || StorageProviderBindingId is null || StorageProviderBindingId == Guid.Empty
            || StorageObjectId.HasValue || objectId == Guid.Empty || string.IsNullOrWhiteSpace(ObjectKey))
            throw new InvalidOperationException("Only a bound upload can stage its durable cleanup identity.");
        StorageObjectId = objectId;
    }

    public void RecordFinalizedResourceVersion(Guid version)
    {
        if (!ExpectedResourceVersion.HasValue || Status != StorageUploadSessionStates.Finalized
            || FinalizedResourceVersion.HasValue || version == Guid.Empty)
            throw new InvalidOperationException("Only a committed resource attachment can bind its replay version.");
        FinalizedResourceVersion = version;
    }

    /// <summary>Acknowledges an exact completed write without reopening a canceled or failed session.</summary>
    public void RecordProducerSettlement(Guid objectId, Guid bindingId, string objectKey, string? providerVersion)
    {
        if (Purpose != StorageObjectPurposes.EventResource || !ExpectedResourceVersion.HasValue
            || objectId == Guid.Empty || bindingId == Guid.Empty
            || StorageObjectId != objectId || StorageProviderBindingId != bindingId
            || !string.Equals(ObjectKey, objectKey, StringComparison.Ordinal)
            || providerVersion is { Length: 0 or > 1024 }
            || ProducerSettled && !string.Equals(ProviderVersionId, providerVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("Producer settlement must match its original bound object identity.");
        ProviderVersionId = providerVersion;
        ProducerSettled = true;
    }

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

        if (Purpose == StorageObjectPurposes.EventResource
            && (!ProducerSettled || StorageObjectId != storageObjectId
                || !string.Equals(ObjectKey, objectKey, StringComparison.Ordinal)))
            throw new InvalidOperationException("Resource attachment requires the exact settled producer identity.");

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
