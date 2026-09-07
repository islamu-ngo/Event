using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.EventOrganizerClaim;

public sealed record EventOrganizerClaimDto
{
    public Guid Id { get; init; }
    [JsonIgnore]
    public Guid TenantId { get; init; }
    public Guid EventId { get; init; }
    public Guid ClaimantActorId { get; init; }
    public string? ClaimantActorDisplayName { get; init; }
    public int StatusId { get; init; }
    public string? StatusCode { get; init; }
    public string? StatusName { get; init; }
    public required string EvidenceType { get; init; }
    public required string EvidenceReference { get; init; }
    public Guid? ReviewerUserId { get; init; }
    public string? DecisionReasonCode { get; init; }
    public DateTime? DecidedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public Guid ConcurrencyStamp { get; init; }

    [JsonIgnore]
    public Guid EventActorId { get; init; }

    [JsonIgnore]
    public Guid? EventActorUserId { get; init; }

    [JsonIgnore]
    public Guid? EventActorOrganizationId { get; init; }

    [JsonIgnore]
    public Guid? EventActorGroupId { get; init; }

    [JsonIgnore]
    public int EventProvenanceTypeId { get; init; }

    [JsonIgnore]
    public string? EventProvenanceTypeCode { get; init; }

    [JsonIgnore]
    public Guid? EventOrganizerActorId { get; init; }

    [JsonIgnore]
    public Guid? EventSubmittedByUserId { get; init; }

    [JsonIgnore]
    public Guid? ClaimantActorUserId { get; init; }

    [JsonIgnore]
    public Guid? ClaimantActorOrganizationId { get; init; }

    [JsonIgnore]
    public Guid? ClaimantActorGroupId { get; init; }
}
