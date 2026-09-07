using Explore.Application.DTOs.CustomPropertyDefinition;
using MediatR;

namespace Explore.Application.Features.CustomPropertyDefinitions.Requests.Queries;

public sealed record GetCustomPropertyDefinitionDetailsRequest(Guid Id = default) : IRequest<CustomPropertyDefinitionDto>;
