using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Federation.Atproto.Requests.Queries;

public sealed record GetPublicEventDiscoveryRequest(GetEventListRequest Criteria)
    : IRequest<PaginatedResult<EventDiscoveryItemDto>>;

public sealed record GetAtprotoEventSourceQuery(Guid AtprotoRecordId) : IRequest<string?>;
