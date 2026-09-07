using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EventReportCase : ITenantEntity, IAuditableEntity, IConcurrencyAware
{
    public const int MaxQueueCodeLength = 50;

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; private set; } = null!;
    public Guid ReportId { get; private set; }
    public EventReport Report { get; private set; } = null!;
    public Guid? CurrentDecisionId { get; private set; }
    public EventReportDecision? CurrentDecision { get; private set; }
    public string QueueCode { get; private set; } = null!;
    public EventReportCaseStatus Status { get; private set; }
    public EventReportPriority Priority { get; private set; }
    public Guid? AssignedModeratorUserId { get; private set; }
    public User? AssignedModeratorUser { get; private set; }
    public DateTime? SlaDueAt { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    public static EventReportCase Create(
        Guid tenantId,
        Guid reportId,
        string queueCode,
        EventReportPriority priority,
        DateTime? slaDueAt,
        DateTime? createdAt = null)
    {
        EventReportGuards.RequireGuid(tenantId, nameof(tenantId), "Tenant id is required.");
        EventReportGuards.RequireGuid(reportId, nameof(reportId), "Report id is required.");
        EventReportGuards.RequireDefined(priority, nameof(priority));

        return new EventReportCase
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            ReportId = reportId,
            QueueCode = EventReportGuards.NormalizeRequired(queueCode, MaxQueueCodeLength, nameof(queueCode)),
            Status = EventReportCaseStatus.Open,
            Priority = priority,
            SlaDueAt = slaDueAt,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
    }

    public void Assign(Guid moderatorUserId, DateTime utcNow)
    {
        EnsureNotClosed();
        EventReportGuards.RequireGuid(moderatorUserId, nameof(moderatorUserId), "Moderator user id is required.");

        AssignedModeratorUserId = moderatorUserId;
        Status = EventReportCaseStatus.Assigned;
        UpdatedAt = utcNow;
    }

    public void MarkWaitingExternal(DateTime utcNow)
    {
        EnsureNotClosed();

        Status = EventReportCaseStatus.WaitingExternal;
        UpdatedAt = utcNow;
    }

    public void MarkWaitingReporter(DateTime utcNow)
    {
        EnsureNotClosed();

        Status = EventReportCaseStatus.WaitingReporter;
        UpdatedAt = utcNow;
    }

    public void MarkDecisionReady(DateTime utcNow)
    {
        EnsureNotClosed();

        Status = EventReportCaseStatus.DecisionReady;
        UpdatedAt = utcNow;
    }

    public bool CanSelectNewDecision()
    {
        if (CurrentDecisionId is null)
        {
            return Status != EventReportCaseStatus.Closed;
        }

        if (CurrentDecision is null
            || CurrentDecision.Execution.State != EventReportDecisionExecutionState.Completed)
        {
            return false;
        }

        return (CurrentDecision.DecisionKind == EventReportDecisionKind.Escalate
                && Status == EventReportCaseStatus.WaitingExternal)
            || (CurrentDecision.DecisionKind == EventReportDecisionKind.NeedsMoreInfo
                && Status == EventReportCaseStatus.WaitingReporter);
    }

    public void SelectDecision(EventReportDecision decision, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(decision);
        EnsureNotClosed();
        if (decision.TenantId != TenantId
            || decision.ReportId != ReportId
            || decision.CaseId != Id)
        {
            throw new InvalidOperationException("The selected decision must belong to this tenant, report, and case.");
        }

        if (!CanSelectNewDecision())
        {
            throw new InvalidOperationException("The current report decision does not permit a replacement decision.");
        }

        CurrentDecisionId = decision.Id;
        CurrentDecision = decision;
        Status = EventReportCaseStatus.DecisionReady;
        UpdatedAt = utcNow;
    }

    public void Close(DateTime utcNow)
    {
        EnsureNotClosed();

        Status = EventReportCaseStatus.Closed;
        UpdatedAt = utcNow;
    }

    public void ChangePriority(EventReportPriority priority, DateTime utcNow)
    {
        EnsureNotClosed();
        EventReportGuards.RequireDefined(priority, nameof(priority));

        Priority = priority;
        UpdatedAt = utcNow;
    }

    public void Triage(string queueCode, EventReportPriority priority, DateTime utcNow)
    {
        EnsureNotClosed();
        EventReportGuards.RequireDefined(priority, nameof(priority));

        QueueCode = EventReportGuards.NormalizeRequired(queueCode, MaxQueueCodeLength, nameof(queueCode));
        Priority = priority;
        UpdatedAt = utcNow;
    }

    private void EnsureNotClosed()
    {
        if (Status == EventReportCaseStatus.Closed)
        {
            throw new InvalidOperationException("Closed report cases cannot be changed.");
        }
    }
}
