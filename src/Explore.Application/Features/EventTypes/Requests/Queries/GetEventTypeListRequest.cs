using Explore.Application.DTOs.EventType;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventTypes.Requests.Queries;

public sealed record GetEventTypeListRequest : IQuery<List<EventTypeListDto>>
{
}
