using Explore.Application.DTOs.EventSessionCustomProperty;
using MediatR;

namespace Explore.Application.Features.EventSessionCustomProperties.Requests.Queries;

public sealed record GetEventSessionCustomPropertyValuesRequest : IRequest<List<EventSessionCustomPropertyValueDto>>
{
    public Guid EventSessionId { get; init; }
}
