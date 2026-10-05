using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Features.Events.Requests.Queries;

namespace Explore.Application.Features.Events.Discovery;

public sealed record GetEventDiscoveryTraversalQuery(GetEventListRequest Criteria, string? Cursor = null)
    : IQuery<EventDiscoveryTraversalDto>;
