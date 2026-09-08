using Explore.Application.DTOs.EventSessionGroup;
using MediatR;

namespace Explore.Application.Features.EventSessionGroups.Requests.Queries;

public sealed record GetEventSessionGroupsByEventRequest(Guid EventId = default)
    : IRequest<List<EventSessionGroupListDto>>;
