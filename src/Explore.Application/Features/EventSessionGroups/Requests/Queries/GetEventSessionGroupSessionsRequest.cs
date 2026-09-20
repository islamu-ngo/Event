using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSession;

namespace Explore.Application.Features.EventSessionGroups.Requests.Queries;

public sealed record GetEventSessionGroupSessionsRequest(Guid EventSessionGroupId = default)
    : IQuery<List<EventSessionListDto>>;
