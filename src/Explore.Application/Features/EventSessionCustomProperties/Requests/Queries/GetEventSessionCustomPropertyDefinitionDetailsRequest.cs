using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSessionCustomProperties.Requests.Queries;

public sealed record GetEventSessionCustomPropertyDefinitionDetailsRequest : IQuery<EventSessionCustomPropertyDefinitionDto>
{
    public Guid Id { get; init; }
}
