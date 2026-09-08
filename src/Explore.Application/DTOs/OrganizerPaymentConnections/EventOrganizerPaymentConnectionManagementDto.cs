using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.OrganizerPaymentConnections;

public sealed record EventOrganizerPaymentConnectionManagementDto
{
    public Guid EventId { get; init; }
    public OrganizerPaymentConnectionDto? Connection { get; init; }

    [JsonIgnore]
    public Guid TenantId { get; init; }

    [JsonIgnore]
    public Guid ActorId { get; init; }

    [JsonIgnore]
    public Guid? ActorUserId { get; init; }

    [JsonIgnore]
    public Guid? ActorOrganizationId { get; init; }

    [JsonIgnore]
    public Guid? ActorGroupId { get; init; }

    [JsonIgnore]
    public Guid OrganizerActorId { get; init; }

    [JsonIgnore]
    public Guid? OrganizerUserId { get; init; }

    [JsonIgnore]
    public Guid? OrganizerOrganizationId { get; init; }

    [JsonIgnore]
    public Guid? OrganizerGroupId { get; init; }
}
