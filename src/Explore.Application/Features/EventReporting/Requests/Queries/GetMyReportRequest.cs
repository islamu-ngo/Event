using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventReporting;

namespace Explore.Application.Features.EventReporting.Requests.Queries;

public sealed record GetMyReportRequest : IQuery<MyEventReportDto?>
{
    public Guid ReportId { get; init; }
}
