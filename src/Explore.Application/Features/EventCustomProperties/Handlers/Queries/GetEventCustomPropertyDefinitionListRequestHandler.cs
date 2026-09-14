using Explore.Application.Mappings;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.Features.EventCustomProperties.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventCustomProperties.Handlers.Queries;

public class GetEventCustomPropertyDefinitionListRequestHandler : IQueryHandler<GetEventCustomPropertyDefinitionListRequest, PaginatedResult<EventCustomPropertyDefinitionListDto>>
{
    private readonly IEventCustomPropertyRepository _eventCustomPropertyRepository;
    private readonly HybridCache _cache;
    private readonly ITenantContext _tenantContext;

    public GetEventCustomPropertyDefinitionListRequestHandler(
        IEventCustomPropertyRepository eventCustomPropertyRepository,
        HybridCache cache,
        ITenantContext tenantContext)
    {
        _eventCustomPropertyRepository = eventCustomPropertyRepository;
        _cache = cache;
        _tenantContext = tenantContext;
    }

    public async Task<PaginatedResult<EventCustomPropertyDefinitionListDto>> QueryAsync(GetEventCustomPropertyDefinitionListRequest request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<EventCustomPropertyDefinitionListDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var tenantId = _tenantContext.TenantId;
        var cacheKey = EventCustomPropertyCache.ListKey(tenantId, request.EventId, pageNumber, pageSize);

        return await _cache.GetOrCreateAsync(
            cacheKey,
            async _ =>
            {
                var (definitions, totalCount) = await _eventCustomPropertyRepository.GetDefinitionsForEventPaged(
                    request.EventId,
                    pageNumber,
                    pageSize);
                var dtos = definitions.Select(CustomPropertyMapper.ToListItem).ToList();
                return PaginatedResult<EventCustomPropertyDefinitionListDto>.Create(dtos, totalCount, pageNumber, pageSize);
            },
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(1)
            },
            tags: [EventCustomPropertyCache.ListsByTenant(tenantId), EventCustomPropertyCache.ListsByEvent(tenantId, request.EventId)],
            cancellationToken: cancellationToken);
    }

}
