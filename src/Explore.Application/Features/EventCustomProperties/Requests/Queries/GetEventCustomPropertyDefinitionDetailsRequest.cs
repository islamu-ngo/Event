using Explore.Application.DTOs.EventCustomProperty;
using MediatR;

namespace Explore.Application.Features.EventCustomProperties.Requests.Queries;

public sealed record GetEventCustomPropertyDefinitionDetailsRequest : IRequest<EventCustomPropertyDefinitionDto>
{
    public Guid Id { get; init; }
}
