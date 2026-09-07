using Explore.Application.DTOs.EventSession;
using MediatR;

namespace Explore.Application.Features.EventSessionGroups.Requests.Queries;

public sealed record GetEventSessionGroupSessionsRequest(Guid EventSessionGroupId = default)
    : IRequest<List<EventSessionListDto>>;
