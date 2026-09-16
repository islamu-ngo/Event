using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventReporting;

namespace Explore.Application.Features.EventReporting.Requests.Queries;

public sealed record GetEventReportOptionsRequest : IQuery<EventReportOptionsDto?>
{
    public Guid EventId { get; init; }
}
