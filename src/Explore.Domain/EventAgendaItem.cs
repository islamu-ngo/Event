using System;
using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public class EventAgendaItem : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public Guid Id { get; set; }

    [ForeignKey("Event")]
    public Guid EventId { get; set; }
    public required Event Event { get; set; }

    [ForeignKey("EventDay")]
    public Guid? EventDayId { get; set; }
    public EventDay? EventDay { get; set; }

    public required string Title { get; set; }
    public string? Description { get; set; }

    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }

    // Cached local projections — written exclusively via ReprojectLocalTimes/Reschedule.
    public DateOnly LocalStartDate { get; private set; }
    public DateOnly LocalEndDate { get; private set; }
    public TimeOnly LocalStartTime { get; private set; }
    public TimeOnly LocalEndTime { get; private set; }
    public int LocalStartMinuteOfDay { get; private set; }
    public int LocalEndMinuteOfDay { get; private set; }

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

    [ForeignKey("Kind")]
    public int? KindId { get; set; }
    public ScheduleItemKind? Kind { get; set; }

    public int SortOrder { get; set; }

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public Guid ConcurrencyStamp { get; set; }

    /// <summary>
    /// Re-projects cached local fields from the current UTC times and the supplied IANA timezone id.
    /// This is the single authorized write path for LocalStart*/LocalEnd* properties.
    /// </summary>
    public void ReprojectLocalTimes(string timezoneId, IEventScheduleProjectionCalculator calculator)
    {
        ArgumentNullException.ThrowIfNull(calculator);
        var projection = calculator.Project(StartTime, EndTime, timezoneId);
        LocalStartDate = projection.LocalStartDate;
        LocalEndDate = projection.LocalEndDate ?? throw new InvalidOperationException("Agenda item projection ended with null LocalEndDate.");
        LocalStartTime = projection.LocalStartTime;
        LocalEndTime = projection.LocalEndTime ?? throw new InvalidOperationException("Agenda item projection ended with null LocalEndTime.");
        LocalStartMinuteOfDay = projection.LocalStartMinuteOfDay;
        LocalEndMinuteOfDay = projection.LocalEndMinuteOfDay ?? throw new InvalidOperationException("Agenda item projection ended with null LocalEndMinuteOfDay.");
    }

    /// <summary>
    /// Reschedules the strict UTC interval and recomputes cached local fields in the event timezone.
    /// </summary>
    public void Reschedule(
        UtcInstantRange schedule,
        string timezoneId,
        IEventScheduleProjectionCalculator calculator)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        StartTime = schedule.Start;
        EndTime = schedule.End;
        ReprojectLocalTimes(timezoneId, calculator);
    }

    public UtcInstantRange GetUtcSchedule() => UtcInstantRange.Create(StartTime, EndTime);

    /// <summary>
    /// Returns the inclusive local-calendar span derived from the cached timezone projection.
    /// </summary>
    public LocalDateRange GetLocalDateRange() => LocalDateRange.Create(LocalStartDate, LocalEndDate);

    public void AssignEventLocation(EventLocation eventLocation)
    {
        ArgumentNullException.ThrowIfNull(eventLocation);
        if (eventLocation.IsDeleted || eventLocation.TenantId != TenantId || eventLocation.EventId != EventId)
        {
            throw new InvalidOperationException("EventLocation must be active and match the agenda item tenant and event.");
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
