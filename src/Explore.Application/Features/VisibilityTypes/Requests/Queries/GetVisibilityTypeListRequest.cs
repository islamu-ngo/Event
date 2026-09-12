using System.Collections.Generic;
using Explore.Application.DTOs.VisibilityType;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.VisibilityTypes.Requests.Queries;

public sealed record GetVisibilityTypeListRequest : IQuery<List<VisibilityTypeListDto>>
{
}
