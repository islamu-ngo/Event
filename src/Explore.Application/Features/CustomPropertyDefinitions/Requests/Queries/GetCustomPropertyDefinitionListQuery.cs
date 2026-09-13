using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.CustomPropertyDefinitions.Requests.Queries;

public sealed record GetCustomPropertyDefinitionListQuery(
    EntityTypeName EntityTypeName = default,
    int PageNumber = 1,
    int PageSize = PaginatedResult<CustomPropertyDefinitionListDto>.DefaultPageSize
) : IQuery<PaginatedResult<CustomPropertyDefinitionListDto>>;
