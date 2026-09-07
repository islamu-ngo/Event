using Explore.Application.DTOs.EventCustomProperty;
using MediatR;

namespace Explore.Application.Features.EventCustomProperties.Requests.Queries;

public sealed record GetEventCustomPropertyValuesRequest : IRequest<List<EventCustomPropertyValueDto>>
{
    public Guid EventId { get; init; }
}
