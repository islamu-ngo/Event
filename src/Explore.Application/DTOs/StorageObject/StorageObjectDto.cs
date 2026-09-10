using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.StorageObject;

public sealed record StorageObjectDto
{
    [JsonIgnore]
    public StorageObjectContentEligibilityDto ContentEligibility { get; init; } = StorageObjectContentEligibilityDto.Unrestricted;

    public StorageObjectDto ForDisclosureAt(DateTime utcNow) => ContentEligibility.CanReadAt(utcNow)
        ? this
        : this with { FullName = string.Empty, SafeDisplayName = string.Empty, Uri = string.Empty };

    public Guid Id { get; init; }
    public int FileTypeId { get; init; }
    public string? FileTypeFullName { get; init; }
    public string? FileTypeMasterCode { get; init; } // For i18n with Tolgee
    public required string Uri { get; init; }
    public required string Provider { get; init; }
    public required string FullName { get; init; }
    public required string SafeDisplayName { get; init; }
    public required string Extension { get; init; }
    public string? ContentType { get; init; }
    public string? Sha256Checksum { get; init; }
    public long Size { get; init; }
    public required string Visibility { get; init; }
    public required string Purpose { get; init; }
    public required string LifecycleState { get; init; }
    public string? OwningResourceKind { get; init; }
    public Guid? OwningResourceId { get; init; }
    public Guid TenantId { get; init; }
    public string? TenantFullName { get; init; }
    public Guid? ActorId { get; init; }
    public string? ActorDisplayName { get; init; }
    public bool IsDeleted { get; init; }
    public DateTime? DeletedAt { get; init; }
    public DateTime? QuarantinedAt { get; init; }
    public string? QuarantineReason { get; init; }
}
