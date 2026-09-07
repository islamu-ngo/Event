using Explore.Domain;

namespace Explore.Application.DTOs.StorageObject;

public sealed record CreateStorageUploadSessionDto
{
    public long ExpectedSizeBytes { get; init; }
    public required string ContentType { get; init; }
    public string? OriginalFileName { get; init; }
    public string? SafeDisplayName { get; init; }
    public string? Extension { get; init; }
    public string Purpose { get; init; } = StorageObjectPurposes.Attachment;
    public string Visibility { get; init; } = StorageObjectVisibilities.PrivateOwner;
    public string? OwningResourceKind { get; init; }
    public Guid? OwningResourceId { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
}
