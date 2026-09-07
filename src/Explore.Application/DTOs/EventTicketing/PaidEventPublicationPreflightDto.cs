using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.EventTicketing;

public sealed record PaidEventPublicationPreflightDto
{
    public Guid EventId { get; init; }
    public Guid? CatalogId { get; init; }
    public bool IsPaidCatalog { get; init; }
    public bool IsReady { get; init; }
    public IReadOnlyList<PaidEventPublicationPreflightBlockerDto> Blockers { get; init; } = [];

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
    public Guid? OrganizerActorId { get; init; }

    [JsonIgnore]
    public Guid? OrganizerUserId { get; init; }

    [JsonIgnore]
    public Guid? OrganizerOrganizationId { get; init; }

    [JsonIgnore]
    public Guid? OrganizerGroupId { get; init; }
}

public sealed record PaidEventPublicationPreflightBlockerDto
{
    public string Code { get; init; } = string.Empty;
    public string Explanation { get; init; } = string.Empty;
}
