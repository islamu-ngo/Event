namespace Explore.Application.DTOs.StorageObject;

public sealed record UpdateStorageObjectDto
{
    public StorageObjectMetadataUpdateDto? Metadata { get; init; }
    public StorageObjectAccessUpdateDto? Access { get; init; }
    public StorageObjectOwnershipUpdateDto? Ownership { get; init; }
}

public sealed record StorageObjectMetadataUpdateDto
{
    public required string FullName { get; init; }
    public string? SafeDisplayName { get; init; }
}

public sealed record StorageObjectAccessUpdateDto
{
    public required string Visibility { get; init; }
    public required string Purpose { get; init; }
}

public sealed record StorageObjectOwnershipUpdateDto
{
    public string? OwningResourceKind { get; init; }
    public Guid? OwningResourceId { get; init; }
    public Guid? ActorId { get; init; }
}
