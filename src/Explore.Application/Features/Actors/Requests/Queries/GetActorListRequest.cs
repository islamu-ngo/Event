using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Actor;
using Explore.Application.Responses;

namespace Explore.Application.Features.Actors.Requests.Queries;

public sealed record GetActorListRequest(
    int PageNumber = 1,
    int PageSize = 20) : IQuery<PaginatedResult<ActorListDto>>;
