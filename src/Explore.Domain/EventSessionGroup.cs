using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EventSessionGroup : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public Guid Id { get; set; }

    [ForeignKey("Event")]
    public Guid EventId { get; set; }
    public required Event Event { get; set; }

    public required string Name { get; set; }
    public string? Slug { get; set; }
    public string? Description { get; set; }

    [ForeignKey(nameof(EventLocation))]
    public Guid? EventLocationId { get; private set; }
    public EventLocation? EventLocation { get; private set; }

    // Legacy physical consistency seam; ELP-330 migrates callers to AssignEventLocation.
    [ForeignKey("Location")]
    public Guid? LocationId { get; set; }
    public Location? Location { get; set; }

    [ForeignKey("Room")]
    public Guid? RoomId { get; set; }
    public LocationRoom? Room { get; set; }

    public string? Color { get; set; }
    public int SortOrder { get; set; }
    public bool IsPublished { get; set; }

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }

    public ICollection<EventSessionGroupSession> Sessions { get; set; } = new List<EventSessionGroupSession>();

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public Guid ConcurrencyStamp { get; set; }

    public void AssignEventLocation(EventLocation eventLocation)
    {
        ArgumentNullException.ThrowIfNull(eventLocation);
        if (eventLocation.IsDeleted || eventLocation.TenantId != TenantId || eventLocation.EventId != EventId)
        {
            throw new InvalidOperationException("EventLocation must be active and match the group tenant and event.");
        }

        var priorLocationId = LocationId;
        EventLocationId = eventLocation.Id;
        EventLocation = eventLocation;
        LocationId = eventLocation.LocationId;
        Location = eventLocation.Location;
        if (LocationId is null || priorLocationId != LocationId)
        {
            RoomId = null;
            Room = null;
        }
    }

    public void DetachEventLocationForDeletion()
    {
        EventLocationId = null;
        EventLocation = null;
        LocationId = null;
        Location = null;
        RoomId = null;
        Room = null;
    }
}
