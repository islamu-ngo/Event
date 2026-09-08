using System.Collections.Generic;
using Explore.Application.DTOs.ActorType;
using MediatR;

namespace Explore.Application.Features.ActorTypes.Requests.Queries;

public sealed record GetActorTypeListRequest : IRequest<List<ActorTypeListDto>>
{
}
