using Explore.Application.DTOs.EventSessionStatus;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSessionStatuses.Requests.Queries;

public sealed record GetEventSessionStatusListQuery : IQuery<List<EventSessionStatusListDto>>
{
}
