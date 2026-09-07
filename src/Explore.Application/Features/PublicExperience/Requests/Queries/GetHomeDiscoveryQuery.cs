using Explore.Application.DTOs.PublicExperience;
using MediatR;

namespace Explore.Application.Features.PublicExperience.Requests.Queries;

public sealed record GetHomeDiscoveryQuery(
    Guid? AreaId = null,
    string? Mode = null) : IRequest<HomeDiscoveryDto>;
