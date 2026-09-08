using Explore.Application.DTOs.EventSessionStatus;
using MediatR;

namespace Explore.Application.Features.EventSessionStatuses.Requests.Queries;

public sealed record GetEventSessionStatusListRequest : IRequest<List<EventSessionStatusListDto>>
{
}
