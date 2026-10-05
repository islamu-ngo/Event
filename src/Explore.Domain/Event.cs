using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Domain.Services.Lifecycle;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.Services.Discovery;

namespace Explore.Domain;

public class Event : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    private readonly List<EventTicketCatalogVersion> _ticketCatalogVersions = [];
    private readonly List<EventCapacityPool> _capacityPools = [];
    private Guid _id;
    private string _title = string.Empty;

    public Event()
    {
    }

    public Event(EventStatusEnum status)
    {
        EventLifecycleRules.EnsureDefinedStatus(status, nameof(status));
        EventStatusId = (int)status;
    }

    public Guid Id
    {
        get => _id;
        set
        {
            _id = value;
            DiscoverySourceSortKey = EventDiscoveryRank.SourceKey(value);
        }
    }

    [ForeignKey("EventType")]
    public int? EventTypeId { get; set; }
    public EventType? EventType { get; set; }

    public required string Title
    {
        get => _title;
        set
        {
            string rank = EventDiscoveryRank.TitleKey(value);
            _title = value;
            DiscoveryTitleSortKey = rank;
        }
    }
    public string DiscoveryTitleSortKey { get; private set; } = string.Empty;
    public string DiscoverySourceSortKey { get; private set; } = EventDiscoveryRank.SourceKey(Guid.Empty);
    public string? Subtitle { get; set; }
    public string? Description { get; set; }
    public string? Content { get; set; }

    [ForeignKey("AudienceGender")]
    public int? AudienceGenderId { get; set; }
    public AudienceGender? AudienceGender { get; set; }

    [ForeignKey("AudienceAge")]
    public int? AudienceAgeId { get; set; }
    public AudienceAge? AudienceAge { get; set; }

    [ForeignKey("Actor")]
    public Guid ActorId { get; set; }
    public required Actor Actor { get; set; }

    [ForeignKey("EventProvenanceType")]
    public int EventProvenanceTypeId { get; set; }
    public EventProvenanceType? EventProvenanceType { get; set; }

    [ForeignKey("SubmittedByUser")]
    public Guid? SubmittedByUserId { get; set; }
    public User? SubmittedByUser { get; set; }

    [ForeignKey("OrganizerActor")]
    public Guid? OrganizerActorId { get; set; }
    public Actor? OrganizerActor { get; set; }

    public string? SourcePublisherName { get; set; }

    [ForeignKey("FeaturedImage")]
    public Guid? FeaturedImageId { get; set; }
    public StorageObject? FeaturedImage { get; set; }

    public int TotalViews { get; set; }

    [ForeignKey("Madhab")]
    public int? MadhabId { get; set; }
    public Madhab? Madhab { get; set; }

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }

    public ICollection<EventSession> Sessions { get; set; } = new List<EventSession>();
    public ICollection<EventSessionGroup> SessionGroups { get; set; } = new List<EventSessionGroup>();
    public ICollection<EventAgendaItem> AgendaItems { get; set; } = new List<EventAgendaItem>();
    public ICollection<EventDay> Days { get; set; } = new List<EventDay>();
    public ICollection<EventModerationRecord> ModerationRecords { get; set; } = new List<EventModerationRecord>();
    public ICollection<EventPublicAction> PublicActions { get; set; } = new List<EventPublicAction>();
    public ICollection<EventOrganizerClaim> OrganizerClaims { get; set; } = new List<EventOrganizerClaim>();
    public EventParticipationConfiguration? ParticipationConfiguration { get; set; }
    public IReadOnlyCollection<EventTicketCatalogVersion> TicketCatalogVersions => _ticketCatalogVersions.AsReadOnly();
    public IReadOnlyCollection<EventCapacityPool> CapacityPools => _capacityPools.AsReadOnly();

    public string? Slug { get; set; }
    public string PublicCode { get; set; } = string.Empty;

    [ForeignKey("VisibilityType")]
    public int VisibilityTypeId { get; set; }
    public required VisibilityType VisibilityType { get; set; }

    public int? SessionCount { get; set; }

    [ForeignKey("EventStatus")]
    public int EventStatusId { get; private set; } = (int)EventStatusEnum.Draft;
    public required EventStatus EventStatus { get; set; }

    public DateOnly? FirstSessionDate { get; set; }
    public DateOnly? LastSessionDate { get; set; }
    public string? Timezone { get; set; }

    public Guid? SourceTemplateId { get; set; }
    public string? SourceTemplateKey { get; set; }
    public int? SourceTemplateVersion { get; set; }
    public DateTimeOffset? InstantiatedFromTemplateAt { get; set; }
    public DateTimeOffset? LastSyncedFromTemplateAt { get; set; }

    // Temporal fields (UTC-based, computed from sessions)
    public DateTimeOffset? FirstSessionStartUtc { get; set; }
    public DateTimeOffset? LastSessionStartUtc { get; set; }
    public DateTimeOffset? LastSessionEndUtc { get; set; }
    public string? EventTimeZoneId { get; set; }

    // Provenance metadata for imported/backfilled events (Task 2.7 lifecycle policy)
    public string? ProvenanceSource { get; set; }
    public string? ProvenanceExternalId { get; set; }

    // Series
    [ForeignKey("EventSeries")]
    public Guid? EventSeriesId { get; set; }
    public EventSeries? EventSeries { get; set; }
    public int? SeriesOrder { get; set; }

    [ForeignKey("EventFormat")]
    public int EventFormatId { get; set; }
    public required EventFormat EventFormat { get; set; }

    [ForeignKey("RegistrationPolicy")]
    public int? RegistrationPolicyId { get; set; }
    public EventRegistrationPolicy? RegistrationPolicy { get; set; }

    [ForeignKey("AtprotoRecord")]
    public Guid? AtprotoRecordId { get; set; }
    public AtprotoRecord? AtprotoRecord { get; set; }

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

    // ===== Aspect Navigation Properties =====
    // Optional 1:1 aspects - only present when event has specific characteristics

    /// <summary>
    /// Islamic aspect for events with Islamic characteristics.
    /// Only populated when event is associated with the Islamic module.
    /// </summary>
    public EventIslamicAspect? IslamicAspect { get; set; }

    /// <summary>
    /// Tech aspect for events with tech/developer characteristics.
    /// Only populated when event is associated with the Tech module.
    /// </summary>
    public EventTechAspect? TechAspect { get; set; }

    // Per-event appearance customization
    public string? BackgroundColor { get; set; }
    public string? BackgroundEffect { get; set; }

    [ForeignKey("BackgroundImage")]
    public Guid? BackgroundImageId { get; set; }
    public StorageObject? BackgroundImage { get; set; }

    public bool Publish(DateTime occurredAt) => TransitionLifecycle(EventStatusEnum.Published, occurredAt, useOrdinaryRules: true);

    public bool Cancel(DateTime occurredAt) => TransitionLifecycle(EventStatusEnum.Cancelled, occurredAt, useOrdinaryRules: true);

    public bool Archive(DateTime occurredAt) => TransitionLifecycle(EventStatusEnum.Archived, occurredAt, useOrdinaryRules: true);

    public bool ApplyLightModeration(DateTime occurredAt) => TransitionLifecycle(EventStatusEnum.Moderated, occurredAt, useOrdinaryRules: true);

    public bool ApplyHeavyModeration(DateTime occurredAt) => TransitionLifecycle(EventStatusEnum.Moderated, occurredAt, useOrdinaryRules: false);

    public bool RestoreAfterLightModeration(DateTime occurredAt)
    {
        DateTime utcOccurredAt = EnsureUtc(occurredAt, nameof(occurredAt));
        EventStatusEnum currentStatus = GetDefinedStatus();
        if (currentStatus == EventStatusEnum.Published)
        {
            return false;
        }

        if (!EventLifecycleRules.CanRestoreAfterLightModeration(currentStatus))
        {
            throw new InvalidOperationException($"Event cannot restore from {currentStatus} after light moderation.");
        }

        return MutateStatus(EventStatusEnum.Published, utcOccurredAt);
    }

    public void EnsureDraftEditable() => EventLifecycleRules.EnsureDraftEditable(GetDefinedStatus());

    public bool SynchronizeFederatedLifecycle(EventStatusEnum status, DateTime occurredAt)
    {
        EventLifecycleRules.EnsureDefinedStatus(status, nameof(status));
        DateTime utcOccurredAt = EnsureUtc(occurredAt, nameof(occurredAt));
        GetDefinedStatus();
        return MutateStatus(status, utcOccurredAt);
    }

    public string GetEffectiveScheduleTimeZoneId()
    {
        return ScheduleTimeZoneResolver.NormalizeOrUtc(EventTimeZoneId ?? Timezone);
    }

    public void ApplyScheduleTimeZone(
        string? timezoneId,
        IEventScheduleProjectionCalculator calculator)
    {
        ArgumentNullException.ThrowIfNull(calculator);

        var normalizedTimeZoneId = ScheduleTimeZoneResolver.NormalizeOrUtc(timezoneId);
        EventTimeZoneId = normalizedTimeZoneId;
        Timezone = normalizedTimeZoneId;

        var daysByDate = Days
            .Where(day => !day.IsDeleted)
            .GroupBy(day => day.LocalDate)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(day => day.SortOrder).ThenBy(day => day.Id).First());

        foreach (var session in Sessions.Where(session => !session.IsDeleted))
        {
            session.ReprojectLocalTimes(normalizedTimeZoneId, calculator);
            session.EventDayId = session.LocalStartDate is not null && daysByDate.TryGetValue(session.LocalStartDate.Value, out var day) ? day.Id : null;
        }

        foreach (var agendaItem in AgendaItems.Where(item => !item.IsDeleted))
        {
            agendaItem.ReprojectLocalTimes(normalizedTimeZoneId, calculator);
            agendaItem.EventDayId = daysByDate.TryGetValue(agendaItem.LocalStartDate, out var day) ? day.Id : null;
        }

        RecalculateScheduleSummaryFromSessions();
    }

    [NotMapped]
    public int? DiscoveryAdditionalSessionCount { get; private set; }

    /// <summary>
    /// Retains only the selected occurrence in a read graph, with a count of other eligible matches.
    /// </summary>
    public void SetDiscoveryOccurrence(EventSession? matchingSession, int matchingSessionCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(matchingSessionCount);
        if ((matchingSession is null) != (matchingSessionCount == 0))
            throw new ArgumentException("A matching occurrence and a positive match count must be supplied together.");
        if (matchingSession is not null
            && (matchingSession.EventId != Id || matchingSession.TenantId != TenantId))
            throw new ArgumentException("The matching occurrence must belong to this event and tenant.", nameof(matchingSession));

        Sessions = matchingSession is null ? [] : [matchingSession];
        DiscoveryAdditionalSessionCount = matchingSessionCount == 0 ? 0 : matchingSessionCount - 1;
    }

    public void RecalculateScheduleSummaryFromSessions()
    {
        var activeSessions = Sessions
            .Where(session => session.ContributesToPublicScheduleSummary())
            .OrderBy(session => session.StartTime)
            .ThenBy(session => session.SortOrder)
            .ThenBy(session => session.Id)
            .ToList();

        SessionCount = activeSessions.Count;

        if (activeSessions.Count == 0)
        {
            FirstSessionDate = null;
            LastSessionDate = null;
            FirstSessionStartUtc = null;
            LastSessionStartUtc = null;
            LastSessionEndUtc = null;
            return;
        }

        var first = activeSessions.First();
        var last = activeSessions.Last();
        FirstSessionDate = first.LocalStartDate;
        LastSessionDate = last.LocalStartDate;
        FirstSessionStartUtc = first.StartTime;
        LastSessionStartUtc = last.StartTime;
        LastSessionEndUtc = activeSessions.Any(session => session.EndTimeType == SessionEndTimeType.OpenEnded && session.EndTime is null)
            ? null
            : activeSessions.Max(session => session.EndTime ?? session.StartTime!.Value.AddDays(1));
    }

    private bool TransitionLifecycle(EventStatusEnum desiredStatus, DateTime occurredAt, bool useOrdinaryRules)
    {
        DateTime utcOccurredAt = EnsureUtc(occurredAt, nameof(occurredAt));
        EventStatusEnum currentStatus = GetDefinedStatus();
        if (useOrdinaryRules)
        {
            EventLifecycleRules.EnsureCanTransition(currentStatus, desiredStatus);
        }

        return MutateStatus(desiredStatus, utcOccurredAt);
    }

    private bool MutateStatus(EventStatusEnum desiredStatus, DateTime occurredAt)
    {
        if ((EventStatusEnum)EventStatusId == desiredStatus)
        {
            return false;
        }

        EventStatusId = (int)desiredStatus;
        UpdatedAt = occurredAt;
        return true;
    }

    private EventStatusEnum GetDefinedStatus()
    {
        EventStatusEnum status = (EventStatusEnum)EventStatusId;
        EventLifecycleRules.EnsureDefinedStatus(status, nameof(EventStatusId));
        return status;
    }

    private static DateTime EnsureUtc(DateTime value, string parameterName)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be a non-default UTC value.", parameterName);
        }

        return value;
    }
}
