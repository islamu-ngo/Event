using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionTemplate;
using Explore.Application.Features.EventSessionTemplates.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessionTemplates.Handlers.Queries;

public class GetEventSessionTemplateListRequestHandler : IQueryHandler<GetEventSessionTemplateListRequest, PaginatedResult<EventSessionTemplateListDto>>
{
    private readonly IEventSessionTemplateRepository _sessionTemplateRepository;
    private readonly HybridCache _cache;
    private readonly IPrivateEventManagementReader? _privateManagement;

    public GetEventSessionTemplateListRequestHandler(
        IEventSessionTemplateRepository sessionTemplateRepository,
        HybridCache cache,
        IPrivateEventManagementReader? privateManagement = null)
    {
        _sessionTemplateRepository = sessionTemplateRepository;
        _cache = cache;
        _privateManagement = privateManagement;
    }

    public async Task<PaginatedResult<EventSessionTemplateListDto>> QueryAsync(GetEventSessionTemplateListRequest request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<EventSessionTemplateListDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        if (_privateManagement is not null)
        {
            var graph = await _privateManagement.ReadAsync(new(PrivateEventManagementReadKind.SessionTemplates), cancellationToken);
            var templates = graph.SessionTemplates.Where(template => template.EventTemplateId == request.EventTemplateId).ToList();
            return PaginatedResult<EventSessionTemplateListDto>.Create(templates.OrderBy(template => template.SortOrder)
                .ThenBy(template => template.DisplayName).Skip(checked((pageNumber - 1) * pageSize)).Take(pageSize)
                .Select(CustomPropertyMapper.ToListItem).ToList(), templates.Count, pageNumber, pageSize);
        }
        var cacheKey = GetCacheKey(request.EventTemplateId, pageNumber, pageSize);

        return await _cache.GetOrCreateAsync(
            cacheKey,
            async _ =>
            {
                var (sessionTemplates, totalCount) = await _sessionTemplateRepository.GetSessionTemplatesPaged(
                    request.EventTemplateId,
                    pageNumber,
                    pageSize);
                var dtos = sessionTemplates.Select(CustomPropertyMapper.ToListItem).ToList();
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
