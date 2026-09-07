using Explore.Application.DTOs.EventReporting;
using MediatR;

namespace Explore.Application.Features.EventReporting.Requests.Queries;

public sealed record GetEventReportOptionsRequest : IRequest<EventReportOptionsDto?>
{
    public Guid EventId { get; init; }
}
