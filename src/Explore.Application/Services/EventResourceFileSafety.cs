using System.Security.Cryptography;
using System.Text;
using Explore.Application.DTOs.EventResource;
using Explore.Domain;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Services;

internal static class EventResourceFileSafety
{
    internal static bool IsSafe(StorageObject storage, Guid tenantId, Guid resourceId,
        EventResourceGovernancePolicy? policy) =>
        IsOwned(storage, tenantId, resourceId)
        && storage.LifecycleState == StorageObjectLifecycleStates.Active
        && storage.Provider is StorageProviders.Local or StorageProviders.S3Compatible
        && storage.StorageProviderBindingId is { } bindingId && bindingId != Guid.Empty
        && !string.IsNullOrWhiteSpace(storage.ObjectKey)
        && storage.HasBoundDocumentInspection
        && policy is { AllowUnscannedDocuments: true }
        && policy.PermittedFileTypes.Contains(storage.ContentType ?? string.Empty)
        && storage.Size > 0 && storage.Size <= policy.MaxUploadBytes;

    internal static EventResourceFileMetadataDto? Describe(EventResource resource, StorageObject? storage) =>
        storage is not null && resource.StorageObjectId == storage.Id
            && IsOwned(storage, resource.TenantId, resource.Id)
        ? new(storage.SafeDisplayName, storage.ContentType, storage.Size, storage.DocumentSafetyState)
        {
            AttachmentGeneration = Generation(storage)
        }
        : null;

    private static bool IsOwned(StorageObject storage, Guid tenantId, Guid resourceId) =>
        storage.TenantId == tenantId && !storage.IsDeleted
        && storage.Purpose == StorageObjectPurposes.EventResource
        && storage.Visibility == StorageObjectVisibilities.PrivateOwner
        && storage.OwningResourceKind == StorageOwningResourceKinds.EventResource
        && storage.OwningResourceId == resourceId;

    internal static string Generation(StorageObject storage) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            storage.Id, storage.ConcurrencyStamp, storage.Provider, storage.ObjectKey,
            storage.StorageProviderBindingId, storage.ProviderVersionId,
            storage.TenantId, storage.IsDeleted, storage.LifecycleState, storage.Purpose,
            storage.Visibility, storage.OwningResourceKind, storage.OwningResourceId,
            storage.Sha256Checksum, storage.Size, storage.ContentType, storage.Extension, storage.SafeDisplayName,
            storage.DocumentSafetyState, storage.InspectedObjectId, storage.InspectedSha256Checksum))));
}
