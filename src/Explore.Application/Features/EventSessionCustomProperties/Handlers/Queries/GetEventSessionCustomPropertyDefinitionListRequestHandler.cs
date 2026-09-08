using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.Features.EventSessionCustomProperties.Requests.Queries;
using Explore.Application.Responses;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessionCustomProperties.Handlers.Queries;

public class GetEventSessionCustomPropertyDefinitionListRequestHandler : IRequestHandler<GetEventSessionCustomPropertyDefinitionListRequest, PaginatedResult<EventSessionCustomPropertyDefinitionListDto>>
{
    private readonly IEventSessionCustomPropertyRepository _sessionCustomPropertyRepository;
    private readonly IMapper _mapper;
    private readonly HybridCache _cache;

    public GetEventSessionCustomPropertyDefinitionListRequestHandler(
        IEventSessionCustomPropertyRepository sessionCustomPropertyRepository,
        IMapper mapper,
        HybridCache cache)
    {
        _sessionCustomPropertyRepository = sessionCustomPropertyRepository;
        _mapper = mapper;
        _cache = cache;
    }

    public async Task<PaginatedResult<EventSessionCustomPropertyDefinitionListDto>> Handle(GetEventSessionCustomPropertyDefinitionListRequest request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<EventSessionCustomPropertyDefinitionListDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var cacheKey = GetCacheKey(request.EventSessionId, pageNumber, pageSize);

        return await _cache.GetOrCreateAsync(
            cacheKey,
            async _ =>
            {
                var (definitions, totalCount) = await _sessionCustomPropertyRepository.GetDefinitionsForSessionPaged(
                    request.EventSessionId,
                    pageNumber,
                    pageSize);
                var dtos = _mapper.Map<List<EventSessionCustomPropertyDefinitionListDto>>(definitions);
                return PaginatedResult<EventSessionCustomPropertyDefinitionListDto>.Create(dtos, totalCount, pageNumber, pageSize);
            },
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(1)
            },
            cancellationToken: cancellationToken);
    }

    private static string GetCacheKey(Guid eventSessionId, int pageNumber, int pageSize)
    {
        return $"session-custom-properties:list:{eventSessionId}:{pageNumber}:{pageSize}";
    }
}
