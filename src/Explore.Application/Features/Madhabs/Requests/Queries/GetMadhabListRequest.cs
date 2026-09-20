using System.Collections.Generic;
using Explore.Application.DTOs.Madhab;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Madhabs.Requests.Queries;

public sealed record GetMadhabListRequest : IQuery<List<MadhabListDto>>
{
}
