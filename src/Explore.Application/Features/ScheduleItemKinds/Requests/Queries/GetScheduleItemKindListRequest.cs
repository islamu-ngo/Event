using Explore.Application.DTOs.ScheduleItemKind;
using MediatR;

namespace Explore.Application.Features.ScheduleItemKinds.Requests.Queries;

public sealed record GetScheduleItemKindListRequest : IRequest<List<ScheduleItemKindListDto>>
{
}
