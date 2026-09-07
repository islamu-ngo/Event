using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.CustomPropertyDefinitions.Handlers.Queries;

public class GetCustomPropertyDefinitionListRequestHandler : IRequestHandler<GetCustomPropertyDefinitionListRequest, PaginatedResult<CustomPropertyDefinitionListDto>>
{
    private readonly ICustomPropertyDefinitionRepository _customPropertyDefinitionRepository;
    private readonly IMapper _mapper;
    private readonly HybridCache _cache;

    public GetCustomPropertyDefinitionListRequestHandler(
        ICustomPropertyDefinitionRepository customPropertyDefinitionRepository,
        IMapper mapper,
        HybridCache cache)
    {
        _customPropertyDefinitionRepository = customPropertyDefinitionRepository;
        _mapper = mapper;
        _cache = cache;
    }

    public async Task<PaginatedResult<CustomPropertyDefinitionListDto>> Handle(GetCustomPropertyDefinitionListRequest request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<CustomPropertyDefinitionListDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var cacheKey = GetCacheKey(request.EntityTypeName, pageNumber, pageSize);

        return await _cache.GetOrCreateAsync(
            cacheKey,
            async _ =>
            {
                var (definitions, totalCount) = await _customPropertyDefinitionRepository.GetDefinitionsWithDetailsPaged(
                    request.EntityTypeName,
                    pageNumber,
                    pageSize);
                var dtos = _mapper.Map<List<CustomPropertyDefinitionListDto>>(definitions);
                return PaginatedResult<CustomPropertyDefinitionListDto>.Create(dtos, totalCount, pageNumber, pageSize);
            },
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(1)
            },
            cancellationToken: cancellationToken);
    }

    private static string GetCacheKey(EntityTypeName entityTypeName, int pageNumber, int pageSize)
    {
        return $"custom-property-definitions:list:{entityTypeName}:{pageNumber}:{pageSize}";
    }
}
