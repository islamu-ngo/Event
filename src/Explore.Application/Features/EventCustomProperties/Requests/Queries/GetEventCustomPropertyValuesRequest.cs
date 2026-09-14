using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventCustomProperties.Requests.Queries;

public sealed record GetEventCustomPropertyValuesRequest : IQuery<List<EventCustomPropertyValueDto>>
{
    public Guid EventId { get; init; }
}
