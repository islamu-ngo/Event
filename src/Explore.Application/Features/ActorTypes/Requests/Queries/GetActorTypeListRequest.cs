using System.Collections.Generic;
using Explore.Application.DTOs.ActorType;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.ActorTypes.Requests.Queries;

public sealed record GetActorTypeListRequest : IQuery<List<ActorTypeListDto>>
{
}
