using Explore.Application.DTOs.Actor;
using MediatR;

namespace Explore.Application.Features.Actors.Requests.Queries;

public sealed record GetActorsByTenantRequest(
    Guid TenantId = default,
    int PageNumber = 1,
    int PageSize = 20) : IRequest<List<ActorListDto>>;
