using Explore.Application.DTOs.Location;

namespace Explore.Application.DTOs.EventSessionAgendaItem;

public sealed record EventSessionAgendaItemListDto
{
    public Guid Id { get; init; }

    // Event
    public Guid EventId { get; init; }

    // Tenant
    public Guid TenantId { get; init; }

    // Event Session
    public Guid EventSessionId { get; init; }
    public string? EventSessionTitle { get; init; }

    // Timing
    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset EndTime { get; init; }

    // Details
    public required string Title { get; init; }
    public string? LocationFullName { get; set; }
    public EventLocationPublicDto? EventLocation { get; set; }
}
