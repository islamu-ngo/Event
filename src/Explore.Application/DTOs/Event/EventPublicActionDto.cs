using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.Event;

public sealed record EventPublicActionDto
{
    public Guid Id { get; init; }
    public Guid EventId { get; init; }
    public Guid ConcurrencyStamp { get; init; }
    public int KindId { get; init; }
    public string? KindCode { get; init; }
    public string? KindName { get; init; }
    public int HealthStateId { get; init; }
    public string? HealthStateCode { get; init; }
    public string? HealthStateName { get; init; }
    public required string Url { get; init; }
    public required string DestinationDomain { get; init; }
    public string? Label { get; init; }
    public int SortOrder { get; init; }
    public bool IsPrimary { get; init; }
    public bool OpenInNewTab => true;
    public string Rel => "noopener noreferrer";

    [JsonIgnore]
    public Guid TenantId { get; init; }

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
}
