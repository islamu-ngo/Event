using Explore.Application.DTOs.Location;

namespace Explore.Application.DTOs.EventSessionAgendaItem;

public sealed record EventSessionAgendaItemDto
{
    public Guid Id { get; init; }
    public Guid EventId { get; init; }
    public Guid EventSessionId { get; init; }
    public string? EventSessionTitle { get; init; }
    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset EndTime { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public Guid? LocationId { get; set; }
    public string? LocationFullName { get; set; }
    public EventLocationPublicDto? EventLocation { get; set; }
    public Guid TenantId { get; init; }
}
