using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Actor;

namespace Explore.Application.Features.Actors.Requests.Queries;

public sealed record GetActorsByTenantRequest(
    Guid TenantId = default,
    int PageNumber = 1,
    int PageSize = 20) : IQuery<List<ActorListDto>>;
