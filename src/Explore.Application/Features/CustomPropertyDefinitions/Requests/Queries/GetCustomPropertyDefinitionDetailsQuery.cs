using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.CustomPropertyDefinitions.Requests.Queries;

public sealed record GetCustomPropertyDefinitionDetailsQuery(Guid Id = default) : IQuery<CustomPropertyDefinitionDto>;
