// ABOUTME: List DTO for storage object metadata used by collection endpoints.
// ABOUTME: Includes provider, visibility, and lifecycle fields needed by local-first UI affordances.

using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.StorageObject;

public sealed record StorageObjectListDto
{
    [JsonIgnore]
    public StorageObjectContentEligibilityDto ContentEligibility { get; init; } = StorageObjectContentEligibilityDto.Unrestricted;

    public StorageObjectListDto ForDisclosureAt(DateTime utcNow) => ContentEligibility.CanReadAt(utcNow)
        ? this
        : this with { FullName = string.Empty, SafeDisplayName = string.Empty, Uri = string.Empty };

    public Guid Id { get; init; }
    public int FileTypeId { get; init; }
    public string? FileTypeFullName { get; init; }
    public required string Uri { get; init; }
    public required string Provider { get; init; }
    public required string FullName { get; init; }
    public required string SafeDisplayName { get; init; }
    public required string Extension { get; init; }
    public string? ContentType { get; init; }
    public long Size { get; init; }
    public required string Visibility { get; init; }
    public required string Purpose { get; init; }
    public required string LifecycleState { get; init; }
    public Guid TenantId { get; init; }
}
