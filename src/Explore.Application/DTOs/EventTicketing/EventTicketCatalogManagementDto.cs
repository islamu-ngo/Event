using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.EventTicketing;

public sealed record EventTicketCatalogManagementDto
{
    public Guid EventId { get; init; }
    public Guid? CatalogId { get; init; }
    public int? VersionNumber { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public int? StatusId { get; init; }
    public string? StatusCode { get; init; }
    public string? StatusName { get; init; }
    public string? MerchantDisclosureText { get; init; }
    public string? RefundPolicyDisclosureText { get; init; }
    public string? SupportContactDisclosureText { get; init; }
    public PaidEventPublicationPreflightDto? PublicationPreflight { get; init; }
    [JsonIgnore]
    public IReadOnlyList<EventTicketTypeDto> TicketTypes { get; init; } = [];
    [JsonIgnore]
    public IReadOnlyList<EventCapacityPoolDto> CapacityPools { get; init; } = [];

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
