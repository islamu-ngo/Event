using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventTemplate;
using Explore.Application.Features.EventTemplates.Requests.Queries;
using Explore.Application.Responses;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventTemplates.Handlers.Queries;

public class GetEventTemplateListRequestHandler : IRequestHandler<GetEventTemplateListRequest, PaginatedResult<EventTemplateListDto>>
{
    private readonly IEventTemplateRepository _eventTemplateRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IMapper _mapper;
    private readonly HybridCache _cache;

    public GetEventTemplateListRequestHandler(
        IEventTemplateRepository eventTemplateRepository,
        ITenantContext tenantContext,
        IMapper mapper,
        HybridCache cache)
    {
        _eventTemplateRepository = eventTemplateRepository;
        _tenantContext = tenantContext;
        _mapper = mapper;
        _cache = cache;
    }

    public async Task<PaginatedResult<EventTemplateListDto>> Handle(GetEventTemplateListRequest request, CancellationToken cancellationToken)
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
                var dtos = _mapper.Map<List<EventTemplateListDto>>(templates);
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
