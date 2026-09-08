using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.CustomPropertyDefinitions.Requests.Queries;

public sealed record GetCustomPropertyDefinitionListRequest(
    EntityTypeName EntityTypeName = default,
    int PageNumber = 1,
    int PageSize = PaginatedResult<CustomPropertyDefinitionListDto>.DefaultPageSize
) : IRequest<PaginatedResult<CustomPropertyDefinitionListDto>>;
