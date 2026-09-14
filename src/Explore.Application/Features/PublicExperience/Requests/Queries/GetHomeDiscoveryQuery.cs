using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.PublicExperience;

namespace Explore.Application.Features.PublicExperience.Requests.Queries;

public sealed record GetHomeDiscoveryQuery(
    Guid? AreaId = null,
    string? Mode = null) : IQuery<HomeDiscoveryDto>;
