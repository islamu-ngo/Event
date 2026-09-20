using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventCustomProperties.Requests.Queries;

public sealed record GetEventCustomPropertyDefinitionDetailsRequest : IQuery<EventCustomPropertyDefinitionDto>
{
    public Guid Id { get; init; }
}
