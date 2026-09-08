using Explore.Application.DTOs.EventSessionStatus;
using MediatR;

namespace Explore.Application.Features.EventSessionStatuses.Requests.Queries;

public sealed record GetEventSessionStatusDetailsRequest : IRequest<EventSessionStatusDto>
{
    public int Id { get; init; }
}
