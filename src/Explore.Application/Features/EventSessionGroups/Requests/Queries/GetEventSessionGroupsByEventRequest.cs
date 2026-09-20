using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionGroup;

namespace Explore.Application.Features.EventSessionGroups.Requests.Queries;

public sealed record GetEventSessionGroupsByEventRequest(Guid EventId = default)
    : IQuery<List<EventSessionGroupListDto>>;
