using Explore.Application.DTOs.EventSessionGroup;
using MediatR;

namespace Explore.Application.Features.EventSessionGroups.Requests.Queries;

public sealed record GetEventSessionGroupDetailRequest(Guid Id = default) : IRequest<EventSessionGroupDto?>;
