using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionTemplate;
using Explore.Application.Features.EventSessionTemplates.Requests.Queries;
using Explore.Application.Responses;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessionTemplates.Handlers.Queries;

public class GetEventSessionTemplateListRequestHandler : IRequestHandler<GetEventSessionTemplateListRequest, PaginatedResult<EventSessionTemplateListDto>>
{
    private readonly IEventSessionTemplateRepository _sessionTemplateRepository;
    private readonly IMapper _mapper;
    private readonly HybridCache _cache;

    public GetEventSessionTemplateListRequestHandler(
        IEventSessionTemplateRepository sessionTemplateRepository,
        IMapper mapper,
        HybridCache cache)
    {
        _sessionTemplateRepository = sessionTemplateRepository;
        _mapper = mapper;
        _cache = cache;
    }

    public async Task<PaginatedResult<EventSessionTemplateListDto>> Handle(GetEventSessionTemplateListRequest request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<EventSessionTemplateListDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var cacheKey = GetCacheKey(request.EventTemplateId, pageNumber, pageSize);

        return await _cache.GetOrCreateAsync(
            cacheKey,
            async _ =>
            {
                var (sessionTemplates, totalCount) = await _sessionTemplateRepository.GetSessionTemplatesPaged(
                    request.EventTemplateId,
                    pageNumber,
                    pageSize);
                var dtos = _mapper.Map<List<EventSessionTemplateListDto>>(sessionTemplates);
                return PaginatedResult<EventSessionTemplateListDto>.Create(dtos, totalCount, pageNumber, pageSize);
            },
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(1)
            },
            cancellationToken: cancellationToken);
    }

    private static string GetCacheKey(Guid eventTemplateId, int pageNumber, int pageSize)
    {
        return $"session-templates:list:{eventTemplateId}:{pageNumber}:{pageSize}";
    }
}
