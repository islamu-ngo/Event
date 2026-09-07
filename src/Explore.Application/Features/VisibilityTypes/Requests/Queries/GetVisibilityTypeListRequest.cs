using System.Collections.Generic;
using Explore.Application.DTOs.VisibilityType;
using MediatR;

namespace Explore.Application.Features.VisibilityTypes.Requests.Queries;

public sealed record GetVisibilityTypeListRequest : IRequest<List<VisibilityTypeListDto>>
{
}
