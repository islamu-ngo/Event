using Explore.Application.DTOs.EventReporting;
using MediatR;

namespace Explore.Application.Features.EventReporting.Requests.Queries;

public sealed record GetMyReportRequest : IRequest<MyEventReportDto?>
{
    public Guid ReportId { get; init; }
}
