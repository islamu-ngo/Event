using System.Linq.Expressions;
using Explore.Domain;

namespace Explore.Application.Specifications.EventReports;

public sealed class EventReportSort : ISortSpecification<EventReport>
{
    private EventReportSort(Expression<Func<EventReport, object>> keySelector)
    {
        KeySelector = keySelector;
    }

    public Expression<Func<EventReport, object>> KeySelector { get; }

    public static EventReportSort CreatedAt => new(report => report.CreatedAt);

    public static EventReportSort UpdatedAt => new(report => report.UpdatedAt ?? report.CreatedAt);

    public static EventReportSort Priority => new(report => report.Priority);

    public static EventReportSort Status => new(report => report.Status);

    public static EventReportSort ReasonCode => new(report => report.ReasonCode);
}
