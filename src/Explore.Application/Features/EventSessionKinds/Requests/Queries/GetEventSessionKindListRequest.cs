using Explore.Application.DTOs.EventSessionKind;
using MediatR;

namespace Explore.Application.Features.EventSessionKinds.Requests.Queries;

public sealed record GetEventSessionKindListRequest : IRequest<List<EventSessionKindListDto>>
{
}
