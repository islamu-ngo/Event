using Explore.Application.Mappings;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.Features.EventSessionCustomProperties.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessionCustomProperties.Handlers.Queries;

public class GetEventSessionCustomPropertyDefinitionListRequestHandler : IQueryHandler<GetEventSessionCustomPropertyDefinitionListRequest, PaginatedResult<EventSessionCustomPropertyDefinitionListDto>>
{
    private readonly IEventSessionCustomPropertyRepository _sessionCustomPropertyRepository;
    private readonly HybridCache _cache;
    private readonly ITenantContext _tenantContext;

    public GetEventSessionCustomPropertyDefinitionListRequestHandler(
        IEventSessionCustomPropertyRepository sessionCustomPropertyRepository,
        HybridCache cache,
        ITenantContext tenantContext)
    {
        _sessionCustomPropertyRepository = sessionCustomPropertyRepository;
        _cache = cache;
        _tenantContext = tenantContext;
    }

    public async Task<PaginatedResult<EventSessionCustomPropertyDefinitionListDto>> QueryAsync(GetEventSessionCustomPropertyDefinitionListRequest request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<EventSessionCustomPropertyDefinitionListDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var tenantId = _tenantContext.TenantId;
        var cacheKey = SessionCustomPropertyCache.ListKey(tenantId, request.EventSessionId, pageNumber, pageSize);

        return await _cache.GetOrCreateAsync(
            cacheKey,
            async _ =>
            {
                var (definitions, totalCount) = await _sessionCustomPropertyRepository.GetDefinitionsForSessionPaged(
                    request.EventSessionId,
                    pageNumber,
                    pageSize);
                var dtos = definitions.Select(CustomPropertyMapper.ToListItem).ToList();
                return PaginatedResult<EventSessionCustomPropertyDefinitionListDto>.Create(dtos, totalCount, pageNumber, pageSize);
            },
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(1)
            },
            tags:
            [
                SessionCustomPropertyCache.ListsByTenant(tenantId),
                SessionCustomPropertyCache.ListsBySession(tenantId, request.EventSessionId)
            ],
            cancellationToken: cancellationToken);
    }
}
