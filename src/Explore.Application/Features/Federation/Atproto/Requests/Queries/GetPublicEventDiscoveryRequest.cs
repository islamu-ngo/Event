using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Responses;

namespace Explore.Application.Features.Federation.Atproto.Requests.Queries;

public sealed record GetPublicEventDiscoveryRequest(GetEventListRequest Criteria)
    : IQuery<PaginatedResult<EventDiscoveryItemDto>>;

public sealed record GetAtprotoEventSourceQuery(Guid AtprotoRecordId) : IQuery<string?>;
