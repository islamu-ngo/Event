using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.Features.EventCustomProperties.Requests.Queries;
using Explore.Application.Responses;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventCustomProperties.Handlers.Queries;

public class GetEventCustomPropertyDefinitionListRequestHandler : IRequestHandler<GetEventCustomPropertyDefinitionListRequest, PaginatedResult<EventCustomPropertyDefinitionListDto>>
{
    private readonly IEventCustomPropertyRepository _eventCustomPropertyRepository;
    private readonly IMapper _mapper;
    private readonly HybridCache _cache;

    public GetEventCustomPropertyDefinitionListRequestHandler(
        IEventCustomPropertyRepository eventCustomPropertyRepository,
        IMapper mapper,
        HybridCache cache)
    {
        _eventCustomPropertyRepository = eventCustomPropertyRepository;
        _mapper = mapper;
        _cache = cache;
    }

    public async Task<PaginatedResult<EventCustomPropertyDefinitionListDto>> Handle(GetEventCustomPropertyDefinitionListRequest request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<EventCustomPropertyDefinitionListDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var cacheKey = GetCacheKey(request.EventId, pageNumber, pageSize);

        return await _cache.GetOrCreateAsync(
            cacheKey,
            async _ =>
            {
                var (definitions, totalCount) = await _eventCustomPropertyRepository.GetDefinitionsForEventPaged(
                    request.EventId,
                    pageNumber,
                    pageSize);
                var dtos = _mapper.Map<List<EventCustomPropertyDefinitionListDto>>(definitions);
                return PaginatedResult<EventCustomPropertyDefinitionListDto>.Create(dtos, totalCount, pageNumber, pageSize);
            },
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(1)
            },
            cancellationToken: cancellationToken);
    }

    private static string GetCacheKey(Guid eventId, int pageNumber, int pageSize)
    {
        return $"event-custom-properties:list:{eventId}:{pageNumber}:{pageSize}";
    }
}
