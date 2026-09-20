using Explore.Application.Caching;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.CustomPropertyDefinitions.Handlers.Queries;

public class GetCustomPropertyDefinitionListQueryHandler : IQueryHandler<GetCustomPropertyDefinitionListQuery, PaginatedResult<CustomPropertyDefinitionListDto>>
{
    private readonly ICustomPropertyDefinitionRepository _customPropertyDefinitionRepository;
    private readonly HybridCache _cache;
    private readonly ITenantContext _tenantContext;

    public GetCustomPropertyDefinitionListQueryHandler(
        ICustomPropertyDefinitionRepository customPropertyDefinitionRepository,
        HybridCache cache,
        ITenantContext tenantContext)
    {
        _customPropertyDefinitionRepository = customPropertyDefinitionRepository;
        _cache = cache;
        _tenantContext = tenantContext;
    }

    public async Task<PaginatedResult<CustomPropertyDefinitionListDto>> QueryAsync(GetCustomPropertyDefinitionListQuery request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<CustomPropertyDefinitionListDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var tenantId = _tenantContext.TenantId;
        var cacheKey = GetCacheKey(tenantId, request.EntityTypeName, pageNumber, pageSize);

        return await _cache.GetOrCreateAsync(
            cacheKey,
            async _ =>
            {
                var (definitions, totalCount) = await _customPropertyDefinitionRepository.GetDefinitionsWithDetailsPaged(
                    request.EntityTypeName,
                    pageNumber,
                    pageSize);
                var dtos = definitions.Select(CustomPropertyMapper.ToListItem).ToList();
                return PaginatedResult<CustomPropertyDefinitionListDto>.Create(dtos, totalCount, pageNumber, pageSize);
            },
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(1)
            },
            tags:
            [
                CacheTags.CustomPropertyDefinitionListsByTenant(tenantId),
                CacheTags.CustomPropertyDefinitionListsByScope(tenantId, request.EntityTypeName)
            ],
            cancellationToken: cancellationToken);
    }

    private static string GetCacheKey(Guid tenantId, EntityTypeName entityTypeName, int pageNumber, int pageSize)
    {
        return $"custom-property-definitions:list:tenant:{tenantId:N}:{entityTypeName}:{pageNumber}:{pageSize}";
    }
}
