using System;
using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EventSessionAgendaItem : ITenantEntity
{
    public Guid Id { get; set; }

    [ForeignKey("EventSession")]
    public Guid EventSessionId { get; set; }
    public required EventSession EventSession { get; set; }

    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public string Title { get; set; }
    public string? Description { get; set; }

    [ForeignKey(nameof(EventLocation))]
    public Guid? EventLocationId { get; private set; }
    public EventLocation? EventLocation { get; private set; }

    // Legacy physical consistency seam; ELP-330 migrates callers to AssignEventLocation.
    [ForeignKey("Location")]
    public Guid? LocationId { get; set; }
    public Location? Location { get; set; }

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }

    public void AssignEventLocation(EventLocation eventLocation)
    {
        ArgumentNullException.ThrowIfNull(eventLocation);
        if (eventLocation.IsDeleted
            || eventLocation.TenantId != TenantId
            || eventLocation.EventId != EventSession.EventId)
        {
            throw new InvalidOperationException("EventLocation must be active and match the session agenda tenant and event.");
        }

        EventLocationId = eventLocation.Id;
        EventLocation = eventLocation;
        LocationId = eventLocation.LocationId;
        Location = eventLocation.Location;
    }
}
