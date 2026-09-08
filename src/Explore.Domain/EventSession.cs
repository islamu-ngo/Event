using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Domain.Services.Lifecycle;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public class EventSession : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public EventSession()
    {
    }

    public EventSession(EventSessionStatusEnum status)
    {
        EventSessionLifecycleRules.EnsureDefinedStatus(status, nameof(status));
        EventSessionStatusId = (int)status;
    }

    public Guid Id { get; set; }
    [ForeignKey("Event")]
    public Guid EventId { get; set; }
    public required Event Event { get; set; }

    [ForeignKey("EventDay")]
    public Guid? EventDayId { get; set; }
    public EventDay? EventDay { get; set; }

    // Null/null represents an unscheduled session. Fixed schedules have a strict range; open-ended and relative schedules may omit EndTime.
    // When StartTime is null, all cached local projections are null.
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public SessionEndTimeType EndTimeType { get; set; } = SessionEndTimeType.Fixed;

    // Cached local projections — written exclusively through semantic scheduling methods or ReprojectLocalTimes.
    // End projections remain null for valid start-only schedules.
    public DateOnly? LocalStartDate { get; private set; }
    public DateOnly? LocalEndDate { get; private set; }
    public TimeOnly? LocalStartTime { get; private set; }
    public TimeOnly? LocalEndTime { get; private set; }
    public int? LocalStartMinuteOfDay { get; private set; }
    public int? LocalEndMinuteOfDay { get; private set; }

    public int SortOrder { get; set; }

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

    public string? Title { get; set; }

    public int? EventSessionKindId { get; set; }
    public EventSessionKind? EventSessionKind { get; set; }

    public int EventSessionStatusId { get; private set; } = (int)EventSessionStatusEnum.Draft;
    public EventSessionStatus? EventSessionStatus { get; set; }

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }
    public string? Slug { get; set; }
    public int? MaxAudienceAttendees { get; set; }
    public int? CurrentAudienceAttendees { get; set; }
    [ForeignKey("RegistrationMode")]
    public int? RegistrationModeId { get; set; }
    public RegistrationMode? RegistrationMode { get; set; }

    [ForeignKey("FeaturedImage")]
    public Guid? FeaturedImageId { get; set; }
    public StorageObject? FeaturedImage { get; set; }

    /// <summary>
    /// Optional Islamic extension stored in a dedicated vertical-partition table.
    /// </summary>
    public EventSessionIslamicAspect? IslamicAspect { get; set; }

    public string? Description { get; set; }

    public Guid? SourceTemplateId { get; set; }
    public string? SourceTemplateKey { get; set; }
    public int? SourceTemplateVersion { get; set; }
    public DateTimeOffset? InstantiatedFromTemplateAt { get; set; }
    public DateTimeOffset? LastSyncedFromTemplateAt { get; set; }

    // Audit fields
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Soft delete fields
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Concurrency control
    public Guid ConcurrencyStamp { get; set; }

    public ICollection<EventSessionGroupSession> SessionGroups { get; set; } = new List<EventSessionGroupSession>();

    public bool Publish(EventStatusEnum parentStatus, DateTime occurredAt)
    {
        if (CurrentStatus == EventSessionStatusEnum.Published)
        {
            return false;
        }

        EnsureUtc(occurredAt, nameof(occurredAt));
        EventLifecycleRules.EnsureDefinedStatus(parentStatus, nameof(parentStatus));
        EventSessionLifecycleRules.EnsureCanPublish(CurrentStatus, parentStatus, StartTime, EndTime, EndTimeType);
        SetStatus(EventSessionStatusEnum.Published, occurredAt);
        return true;
    }

    public bool Cancel(EventStatusEnum parentStatus, DateTime occurredAt)
    {
        if (CurrentStatus == EventSessionStatusEnum.Cancelled)
        {
            return false;
        }

        EnsureUtc(occurredAt, nameof(occurredAt));
        EventLifecycleRules.EnsureDefinedStatus(parentStatus, nameof(parentStatus));
        EventSessionLifecycleRules.EnsureCanCancel(CurrentStatus, parentStatus);
        SetStatus(EventSessionStatusEnum.Cancelled, occurredAt);
        return true;
    }

    public bool Complete(EventStatusEnum parentStatus, DateTime occurredAt)
    {
        if (CurrentStatus == EventSessionStatusEnum.Completed)
        {
            return false;
        }

        EnsureUtc(occurredAt, nameof(occurredAt));
        EventLifecycleRules.EnsureDefinedStatus(parentStatus, nameof(parentStatus));
        EventSessionLifecycleRules.EnsureCanComplete(CurrentStatus, parentStatus);
        SetStatus(EventSessionStatusEnum.Completed, occurredAt);
        return true;
    }

    public bool Archive(EventStatusEnum parentStatus, DateTime occurredAt)
    {
        if (CurrentStatus == EventSessionStatusEnum.Archived)
        {
            return false;
        }

        EnsureUtc(occurredAt, nameof(occurredAt));
        EventLifecycleRules.EnsureDefinedStatus(parentStatus, nameof(parentStatus));
        EventSessionLifecycleRules.EnsureCanArchive(CurrentStatus, parentStatus);
        SetStatus(EventSessionStatusEnum.Archived, occurredAt);
        return true;
    }

    public bool ApplyParentModeration(DateTime occurredAt)
    {
        if (CurrentStatus == EventSessionStatusEnum.Moderated)
        {
            return false;
        }

        EnsureUtc(occurredAt, nameof(occurredAt));
        SetStatus(EventSessionStatusEnum.Moderated, occurredAt);
        return true;
    }

    public bool SynchronizeFederatedLifecycle(EventSessionStatusEnum status, DateTime occurredAt)
    {
        if (CurrentStatus == status)
        {
            return false;
        }

        EnsureUtc(occurredAt, nameof(occurredAt));
        EventSessionLifecycleRules.EnsureDefinedStatus(status, nameof(status));
        SetStatus(status, occurredAt);
        return true;
    }

    /// <summary>
    /// Re-projects cached local fields from the current UTC times and the supplied IANA timezone id.
    /// This is the single authorized write path for LocalStart*/LocalEnd* properties.
    /// Handlers, validators, mappers, and seeders must not write those fields directly.
    /// </summary>
    public void ReprojectLocalTimes(string timezoneId, IEventScheduleProjectionCalculator calculator)
    {
        ArgumentNullException.ThrowIfNull(calculator);

        // Unscheduled sessions: clear all local projections so DB check constraints pass.
        if (StartTime is null)
        {
            LocalStartDate = null;
            LocalEndDate = null;
            LocalStartTime = null;
            LocalEndTime = null;
            LocalStartMinuteOfDay = null;
            LocalEndMinuteOfDay = null;
            return;
        }

        var projection = calculator.Project(StartTime.Value, EndTime, timezoneId);
        LocalStartDate = projection.LocalStartDate;
        LocalEndDate = projection.LocalEndDate;
        LocalStartTime = projection.LocalStartTime;
        LocalEndTime = projection.LocalEndTime;
        LocalStartMinuteOfDay = projection.LocalStartMinuteOfDay;
        LocalEndMinuteOfDay = projection.LocalEndMinuteOfDay;
    }

    /// <summary>
    /// Reschedules a fixed strict UTC interval and recomputes cached local fields in the event timezone.
    /// </summary>
    public void Reschedule(
        UtcInstantRange schedule,
        string timezoneId,
        IEventScheduleProjectionCalculator calculator)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        EventSessionLifecycleRules.EnsureCanSchedule(CurrentStatus);

        EndTimeType = SessionEndTimeType.Fixed;
        StartTime = schedule.Start;
        EndTime = schedule.End;
        ReprojectLocalTimes(timezoneId, calculator);
    }

    public void ScheduleOpenEnded(
        DateTimeOffset startUtc,
        string timezoneId,
        IEventScheduleProjectionCalculator calculator)
    {
        ApplyStartOnlySchedule(startUtc, SessionEndTimeType.OpenEnded, timezoneId, calculator);
    }

    public void ScheduleRelativeToPrayer(
        DateTimeOffset startUtc,
        string timezoneId,
        IEventScheduleProjectionCalculator calculator)
    {
        ApplyStartOnlySchedule(startUtc, SessionEndTimeType.RelativeToPrayer, timezoneId, calculator);
    }

    public void ScheduleRelativeToPrayer(
        UtcInstantRange schedule,
        string timezoneId,
        IEventScheduleProjectionCalculator calculator)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        EventSessionLifecycleRules.EnsureCanSchedule(CurrentStatus);

        EndTimeType = SessionEndTimeType.RelativeToPrayer;
        StartTime = schedule.Start;
        EndTime = schedule.End;
        ReprojectLocalTimes(timezoneId, calculator);
    }

    public void Unschedule()
    {
        EventSessionLifecycleRules.EnsureCanSchedule(CurrentStatus);
        StartTime = null;
        EndTime = null;
        ClearLocalScheduleProjection();
    }

    public UtcInstantRange? GetUtcSchedule() =>
        StartTime is not null && EndTime is not null
            ? UtcInstantRange.Create(StartTime.Value, EndTime.Value)
            : null;

    /// <summary>
    /// Returns the inclusive local-calendar span when both projected boundaries exist.
    /// </summary>
    public LocalDateRange? GetLocalDateRange() =>
        LocalStartDate is not null && LocalEndDate is not null
            ? LocalDateRange.Create(LocalStartDate.Value, LocalEndDate.Value)
            : null;

    private void ApplyStartOnlySchedule(
        DateTimeOffset startUtc,
        SessionEndTimeType endTimeType,
        string timezoneId,
        IEventScheduleProjectionCalculator calculator)
    {
        EventSessionLifecycleRules.EnsureCanSchedule(CurrentStatus);

        StartTime = startUtc.ToUniversalTime();
        EndTime = null;
        EndTimeType = endTimeType;
        ReprojectLocalTimes(timezoneId, calculator);
    }

    private void ClearLocalScheduleProjection()
    {
        LocalStartDate = null;
        LocalEndDate = null;
        LocalStartTime = null;
        LocalEndTime = null;
        LocalStartMinuteOfDay = null;
        LocalEndMinuteOfDay = null;
    }

    public bool ContributesToPublicScheduleSummary()
    {
        return !IsDeleted
            && EventSessionStatusId == (int)EventSessionStatusEnum.Published
            && StartTime is not null
            && (EndTimeType == SessionEndTimeType.OpenEnded || EndTimeType == SessionEndTimeType.RelativeToPrayer || EndTime is not null);
    }

    public void AssignEventLocation(EventLocation eventLocation)
    {
        ArgumentNullException.ThrowIfNull(eventLocation);
        if (eventLocation.IsDeleted || eventLocation.TenantId != TenantId || eventLocation.EventId != EventId)
        {
            throw new InvalidOperationException("EventLocation must be active and match the session tenant and event.");
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

    private EventSessionStatusEnum CurrentStatus => (EventSessionStatusEnum)EventSessionStatusId;

    private static void EnsureUtc(DateTime timestamp, string parameterName)
    {
        if (timestamp.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Event session lifecycle timestamps must be UTC.", parameterName);
        }
    }

    private void SetStatus(EventSessionStatusEnum status, DateTime occurredAt)
    {
        EventSessionStatusId = (int)status;
        UpdatedAt = occurredAt;
    }
}

public enum SessionStartTimeType
{
    Fixed = 0,
    RelativeToPrayer = 1
}
