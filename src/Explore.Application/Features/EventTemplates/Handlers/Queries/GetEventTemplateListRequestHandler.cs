using Explore.Application.Mappings;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventTemplate;
using Explore.Application.Features.EventTemplates.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventTemplates.Handlers.Queries;

public class GetEventTemplateListRequestHandler : IQueryHandler<GetEventTemplateListRequest, PaginatedResult<EventTemplateListDto>>
{
    private readonly IEventTemplateRepository _eventTemplateRepository;
    private readonly ITenantContext _tenantContext;
    private readonly HybridCache _cache;

    public GetEventTemplateListRequestHandler(
        IEventTemplateRepository eventTemplateRepository,
        ITenantContext tenantContext,
        HybridCache cache)
    {
        _eventTemplateRepository = eventTemplateRepository;
        _tenantContext = tenantContext;
        _cache = cache;
    }

    public async Task<PaginatedResult<EventTemplateListDto>> QueryAsync(GetEventTemplateListRequest request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<EventTemplateListDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var cacheKey = GetCacheKey(_tenantContext.TenantId, request.EventTypeId, pageNumber, pageSize);

        return await _cache.GetOrCreateAsync(
            cacheKey,
            async _ =>
            {
                var (templates, totalCount) = await _eventTemplateRepository.GetTemplatesPaged(
                    _tenantContext.TenantId,
                    request.EventTypeId,
                    pageNumber,
                    pageSize);
                var dtos = templates.Select(CustomPropertyMapper.ToListItem).ToList();
                return PaginatedResult<EventTemplateListDto>.Create(dtos, totalCount, pageNumber, pageSize);
            },
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(1)
            },
            cancellationToken: cancellationToken);
    }

    private static string GetCacheKey(Guid tenantId, int? eventTypeId, int pageNumber, int pageSize)
    {
        return $"event-templates:list:{tenantId}:{eventTypeId}:{pageNumber}:{pageSize}";
    }
}
