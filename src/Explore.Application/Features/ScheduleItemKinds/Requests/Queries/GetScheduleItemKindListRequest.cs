using Explore.Application.DTOs.ScheduleItemKind;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.ScheduleItemKinds.Requests.Queries;

public sealed record GetScheduleItemKindListRequest : IQuery<List<ScheduleItemKindListDto>>
{
}
