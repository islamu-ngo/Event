using System.Collections.Generic;
using Explore.Application.DTOs.GroupPosition;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.GroupPositions.Requests.Queries;

public sealed record GetGroupPositionListRequest : IQuery<List<GroupPositionListDto>>
{
}
