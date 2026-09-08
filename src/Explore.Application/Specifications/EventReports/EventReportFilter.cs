using System.Linq.Expressions;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Specifications.EventReports;

public sealed class EventReportFilter : IFilterSpecification<EventReport>
{
    private EventReportFilter(EventReportFilterType filterType, Expression<Func<EventReport, bool>> predicate)
    {
        FilterType = filterType;
        Predicate = predicate;
    }

    public EventReportFilterType FilterType { get; }

    public Expression<Func<EventReport, bool>> Predicate { get; }

    public static EventReportFilter Event(Guid eventId) =>
        new(EventReportFilterType.Event, report => report.EventId == eventId);

    public static EventReportFilter ReporterUser(Guid reporterUserId) =>
        new(EventReportFilterType.ReporterUser, report => report.ReporterUserId == reporterUserId);

    public static EventReportFilter Status(EventReportStatus status) =>
        new(EventReportFilterType.Status, report => report.Status == status);

    public static EventReportFilter Statuses(IReadOnlyCollection<EventReportStatus> statuses)
    {
        var normalizedStatuses = statuses.Distinct().ToArray();
        return new(EventReportFilterType.Statuses, report => normalizedStatuses.Contains(report.Status));
    }

    public static EventReportFilter CaseStatuses(IReadOnlyCollection<EventReportCaseStatus> statuses)
    {
        var normalizedStatuses = statuses.Distinct().ToArray();
        return new(EventReportFilterType.CaseStatuses, report => report.Cases.Any(reportCase => normalizedStatuses.Contains(reportCase.Status)));
    }

    public static EventReportFilter Priority(EventReportPriority priority) =>
        new(EventReportFilterType.Priority, report => report.Priority == priority);

    public static EventReportFilter ReasonCode(string reasonCode)
    {
        var normalizedReasonCode = reasonCode.Trim();
        return new(EventReportFilterType.ReasonCode, report => report.ReasonCode == normalizedReasonCode);
    }

    public static EventReportFilter CreatedFrom(DateTime createdFromUtc) =>
        new(EventReportFilterType.CreatedFrom, report => report.CreatedAt >= createdFromUtc);

    public static EventReportFilter CreatedTo(DateTime createdToUtc) =>
        new(EventReportFilterType.CreatedTo, report => report.CreatedAt <= createdToUtc);

    public static EventReportFilter QueueCode(string queueCode)
    {
        var normalizedQueueCode = queueCode.Trim();
        return new(EventReportFilterType.QueueCode, report => report.Cases.Any(reportCase => reportCase.QueueCode == normalizedQueueCode));
    }

    public static EventReportFilter AssignedTo(Guid moderatorUserId) =>
        new(EventReportFilterType.AssignedTo, report => report.Cases.Any(reportCase => reportCase.AssignedModeratorUserId == moderatorUserId));

    public static EventReportFilter Unassigned() =>
        new(EventReportFilterType.Unassigned, report => report.Cases.Any(reportCase => reportCase.AssignedModeratorUserId == null));

    public static EventReportFilter OpenQueueItems() =>
        new(EventReportFilterType.OpenQueueItems, report => report.Cases.Any(reportCase => reportCase.Status != EventReportCaseStatus.Closed));
}

public enum EventReportFilterType
{
    Event,
    ReporterUser,
    Status,
    Statuses,
    CaseStatuses,
    Priority,
    ReasonCode,
    CreatedFrom,
    CreatedTo,
    QueueCode,
    AssignedTo,
    Unassigned,
    OpenQueueItems
}
