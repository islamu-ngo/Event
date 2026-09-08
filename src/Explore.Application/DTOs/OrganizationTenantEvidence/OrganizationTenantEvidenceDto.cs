using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.OrganizationTenantEvidence;

public sealed record OrganizationTenantEvidenceDto
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid DocumentStorageObjectId { get; init; }
    public required string DocumentDisplayName { get; init; }
    public string? DocumentContentType { get; init; }
    public long DocumentSizeBytes { get; init; }
    public int ReviewStatusId { get; init; }
    public string? ReviewStatusCode { get; init; }
    public string? ReviewStatusName { get; init; }
    public string? ReviewNotes { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public Guid ConcurrencyStamp { get; init; }

    [JsonIgnore]
    public Guid TenantId { get; init; }

    [JsonIgnore]
    public Guid OrganizationTenantId { get; init; }

    [JsonIgnore]
    public Guid? DocumentCreatedBy { get; init; }
}
