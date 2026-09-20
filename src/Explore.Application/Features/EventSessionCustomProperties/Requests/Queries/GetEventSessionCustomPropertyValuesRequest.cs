using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSessionCustomProperties.Requests.Queries;

public sealed record GetEventSessionCustomPropertyValuesRequest : IQuery<List<EventSessionCustomPropertyValueDto>>
{
    public Guid EventSessionId { get; init; }
}
