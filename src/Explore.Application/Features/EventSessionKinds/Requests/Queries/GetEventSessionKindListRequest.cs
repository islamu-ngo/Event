using Explore.Application.DTOs.EventSessionKind;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSessionKinds.Requests.Queries;

public sealed record GetEventSessionKindListRequest : IQuery<List<EventSessionKindListDto>>
{
}
