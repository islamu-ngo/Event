using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EventReport : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    private const int MaxReasonCodeLength = 100;
    private const int MaxSubcategoryCodeLength = 100;
    private const int MaxLocaleLength = 10;
    private const int MaxHashLength = 64;

    private static readonly IReadOnlyDictionary<EventReportStatus, EventReportStatus[]> AllowedTransitions =
        new Dictionary<EventReportStatus, EventReportStatus[]>
        {
            [EventReportStatus.Submitted] =
            [
                EventReportStatus.Triaged,
                EventReportStatus.UnderReview,
                EventReportStatus.Actioned,
                EventReportStatus.Dismissed,
                EventReportStatus.Duplicate,
                EventReportStatus.Escalated,
                EventReportStatus.Closed
            ],
            [EventReportStatus.Triaged] =
            [
                EventReportStatus.UnderReview,
                EventReportStatus.Actioned,
                EventReportStatus.Dismissed,
                EventReportStatus.Duplicate,
                EventReportStatus.Escalated,
                EventReportStatus.Closed
            ],
            [EventReportStatus.UnderReview] =
            [
                EventReportStatus.Actioned,
                EventReportStatus.Dismissed,
                EventReportStatus.Duplicate,
                EventReportStatus.Escalated,
                EventReportStatus.Closed
            ],
            [EventReportStatus.Escalated] =
            [
                EventReportStatus.UnderReview,
                EventReportStatus.Actioned,
                EventReportStatus.Dismissed,
                EventReportStatus.Duplicate,
                EventReportStatus.Closed
            ],
            [EventReportStatus.Actioned] = [EventReportStatus.Closed],
            [EventReportStatus.Dismissed] = [EventReportStatus.Closed],
            [EventReportStatus.Duplicate] = [EventReportStatus.Closed],
            [EventReportStatus.Closed] = []
        };

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; private set; } = null!;
    public Guid EventId { get; private set; }
    public Event Event { get; private set; } = null!;
    public Guid? ReporterUserId { get; private set; }
    public User? ReporterUser { get; private set; }
    public Guid? ReporterActorId { get; private set; }
    public Actor? ReporterActor { get; private set; }
    public EventReporterKind ReporterKind { get; private set; }
    public EventReportSourceKind SourceKind { get; private set; }
    public string ReasonCode { get; private set; } = null!;
    public string? SubcategoryCode { get; private set; }
    public EventReportStatus Status { get; private set; }
    public EventReportPriority Priority { get; private set; }
    public EventReportSeverityHint? SeverityHint { get; private set; }
    public Guid? DuplicateGroupId { get; private set; }
    public bool ReportCaseUpdatesConsent { get; private set; }
    public bool ReportFollowUpContactConsent { get; private set; }
    public string? ReporterLocale { get; private set; }
    public string? ReporterIpHash { get; private set; }
    public string? ReporterUserAgentHash { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public ICollection<EventReportTarget> Targets { get; private set; } = new List<EventReportTarget>();
    public ICollection<EventReportEvidence> EvidenceItems { get; private set; } = new List<EventReportEvidence>();
    public ICollection<EventReportCase> Cases { get; private set; } = new List<EventReportCase>();
    public ICollection<EventReportSignal> Signals { get; private set; } = new List<EventReportSignal>();
    public ICollection<EventReportDecision> Decisions { get; private set; } = new List<EventReportDecision>();
    public ICollection<EventReportExternalLink> ExternalLinks { get; private set; } = new List<EventReportExternalLink>();
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    public bool IsTerminal => IsTerminalStatus(Status);

    public static EventReport Create(
        Guid tenantId,
        Guid eventId,
        Guid? reporterUserId,
        Guid? reporterActorId,
        EventReporterKind reporterKind,
        EventReportSourceKind sourceKind,
        string reasonCode,
        string? subcategoryCode,
        EventReportPriority priority,
        EventReportSeverityHint? severityHint,
        bool reportCaseUpdatesConsent,
        bool reportFollowUpContactConsent,
        string? reporterLocale,
        string? reporterIpHash,
        string? reporterUserAgentHash,
        DateTime? createdAt = null)
    {
        EventReportGuards.RequireGuid(tenantId, nameof(tenantId), "Tenant id is required.");
        EventReportGuards.RequireGuid(eventId, nameof(eventId), "Event id is required.");
        EventReportGuards.RequireDefined(reporterKind, nameof(reporterKind));
        EventReportGuards.RequireDefined(sourceKind, nameof(sourceKind));
        EventReportGuards.RequireDefined(priority, nameof(priority));

        if (severityHint is not null)
        {
            EventReportGuards.RequireDefined(severityHint.Value, nameof(severityHint));
        }

        if (reporterUserId == Guid.Empty)
        {
            throw new ArgumentException("Reporter user id cannot be empty.", nameof(reporterUserId));
        }

        if (reporterActorId == Guid.Empty)
        {
            throw new ArgumentException("Reporter actor id cannot be empty.", nameof(reporterActorId));
        }

        return new EventReport
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            EventId = eventId,
            ReporterUserId = reporterUserId,
            ReporterActorId = reporterActorId,
            ReporterKind = reporterKind,
            SourceKind = sourceKind,
            ReasonCode = EventReportGuards.NormalizeRequired(reasonCode, MaxReasonCodeLength, nameof(reasonCode)),
            SubcategoryCode = EventReportGuards.NormalizeOptional(subcategoryCode, MaxSubcategoryCodeLength, nameof(subcategoryCode)),
            Status = EventReportStatus.Submitted,
            Priority = priority,
            SeverityHint = severityHint,
            ReportCaseUpdatesConsent = reportCaseUpdatesConsent,
            ReportFollowUpContactConsent = reportFollowUpContactConsent,
            ReporterLocale = EventReportGuards.NormalizeOptional(reporterLocale, MaxLocaleLength, nameof(reporterLocale)),
            ReporterIpHash = EventReportGuards.NormalizeOptional(reporterIpHash, MaxHashLength, nameof(reporterIpHash)),
            ReporterUserAgentHash = EventReportGuards.NormalizeOptional(reporterUserAgentHash, MaxHashLength, nameof(reporterUserAgentHash)),
            CreatedAt = createdAt ?? DateTime.UtcNow,
            CreatedBy = reporterUserId,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
    }

    public void ChangeReporterCommunicationConsent(
        bool reportCaseUpdatesConsent,
        bool reportFollowUpContactConsent,
        DateTime utcNow)
    {
        if (ReportCaseUpdatesConsent == reportCaseUpdatesConsent
            && ReportFollowUpContactConsent == reportFollowUpContactConsent)
        {
            return;
        }

        ReportCaseUpdatesConsent = reportCaseUpdatesConsent;
        ReportFollowUpContactConsent = reportFollowUpContactConsent;
        UpdatedAt = utcNow;
    }

    public void UpdateStatus(EventReportStatus nextStatus, DateTime utcNow)
    {
        EventReportGuards.RequireDefined(nextStatus, nameof(nextStatus));

        if (Status == nextStatus)
        {
            return;
        }

        if (IsTerminal)
        {
            throw new InvalidOperationException("Terminal event reports cannot transition to another status.");
        }

        if (!AllowedTransitions.TryGetValue(Status, out var allowed) || !allowed.Contains(nextStatus))
        {
            throw new InvalidOperationException($"Event reports cannot transition from {Status} to {nextStatus}.");
        }

        Status = nextStatus;
        UpdatedAt = utcNow;

        if (IsTerminalStatus(nextStatus))
        {
            ClosedAt = utcNow;
        }
    }

    public void MarkDuplicate(Guid duplicateGroupId, DateTime utcNow)
    {
        EventReportGuards.RequireGuid(duplicateGroupId, nameof(duplicateGroupId), "Duplicate group id is required.");

        DuplicateGroupId = duplicateGroupId;
        UpdateStatus(EventReportStatus.Duplicate, utcNow);
    }

    public void ChangePriority(EventReportPriority priority, DateTime utcNow)
    {
        EventReportGuards.RequireDefined(priority, nameof(priority));

        if (IsTerminal)
        {
            throw new InvalidOperationException("Terminal event reports cannot change priority.");
        }

        Priority = priority;
        UpdatedAt = utcNow;
    }

    private static bool IsTerminalStatus(EventReportStatus status)
    {
        return status is EventReportStatus.Actioned
            or EventReportStatus.Dismissed
            or EventReportStatus.Duplicate
            or EventReportStatus.Closed;
    }
}
