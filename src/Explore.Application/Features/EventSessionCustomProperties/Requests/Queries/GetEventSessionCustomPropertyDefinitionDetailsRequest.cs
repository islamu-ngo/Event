using Explore.Application.DTOs.EventSessionCustomProperty;
using MediatR;

namespace Explore.Application.Features.EventSessionCustomProperties.Requests.Queries;

public sealed record GetEventSessionCustomPropertyDefinitionDetailsRequest : IRequest<EventSessionCustomPropertyDefinitionDto>
{
    public Guid Id { get; init; }
}
