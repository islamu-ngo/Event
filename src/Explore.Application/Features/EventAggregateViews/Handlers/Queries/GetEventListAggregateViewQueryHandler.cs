using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventAggregateView;
using Explore.Application.Features.EventAggregateViews.Requests.Queries;
using Explore.Application.Responses;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventAggregateViews.Handlers.Queries;

public sealed class GetEventListAggregateViewQueryHandler
    : IRequestHandler<GetEventListAggregateViewQuery, BaseCommandResponse<PaginatedResult<EventListAggregateViewDto>>>
{
    private readonly IEventAggregateViewRepository _repository;
    private readonly HybridCache _cache;
    private readonly ILogger<GetEventListAggregateViewQueryHandler> _logger;

    public GetEventListAggregateViewQueryHandler(
        IEventAggregateViewRepository repository,
        HybridCache cache,
        ILogger<GetEventListAggregateViewQueryHandler> logger)
    {
        _repository = repository;
        _cache = cache;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<PaginatedResult<EventListAggregateViewDto>>> Handle(
        GetEventListAggregateViewQuery request,
        CancellationToken cancellationToken)
    {
        var validator = new GetEventListAggregateViewQueryValidator();
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        var (pageNumber, pageSize) = PaginatedResult<EventListAggregateViewDto>.NormalizeParameters(request.Page, request.PageSize);
        var filter = request.Filter ?? new AggregateViewFilterDto();
        var repositoryFilter = ToRepositoryFilter(filter);
        var cacheKey = $"event-aggregate:list:{pageNumber}:{pageSize}:{request.ExposureCeiling}:{BuildFilterCacheKey(filter)}";

        return await _cache.GetOrCreateAsync(
            cacheKey,
            async _ =>
            {
                var (items, totalCount) = await _repository.GetPagedAsync(repositoryFilter, pageNumber, pageSize, cancellationToken);
                var definitionLookup = (await _repository.GetEventDefinitionsByEventIdsAsync(items.Select(x => x.EventId).ToList(), cancellationToken))
                    .GroupBy(x => x.EventId)
                    .ToDictionary(x => x.Key, x => (IReadOnlyCollection<Explore.Domain.EventCustomPropertyDefinition>)x.ToList());

                var dtos = items
                    .Select(item => EventAggregateViewMapper.MapListItem(
                        item,
                        definitionLookup.TryGetValue(item.EventId, out var definitions) ? definitions : [],
                        request.ExposureCeiling,
                        _logger))
                    .ToList();

                return BaseCommandResponse.Success(
                    PaginatedResult<EventListAggregateViewDto>.Create(dtos, totalCount, pageNumber, pageSize),
                    "Event aggregate list retrieved successfully.");
            },
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(2),
                LocalCacheExpiration = TimeSpan.FromSeconds(30)
            },
            cancellationToken: cancellationToken);
    }

    private static EventAggregateViewFilter ToRepositoryFilter(AggregateViewFilterDto filter)
        => new(filter.Title, filter.StartAtFrom, filter.StartAtTo, filter.Status, filter.Visibility);

    private static string BuildFilterCacheKey(AggregateViewFilterDto filter)
        => string.Join(
            '|',
            (filter.Title ?? string.Empty).Trim().ToLowerInvariant(),
            filter.StartAtFrom?.ToString("O") ?? string.Empty,
            filter.StartAtTo?.ToString("O") ?? string.Empty,
            (filter.Status ?? string.Empty).Trim().ToLowerInvariant(),
            (filter.Visibility ?? string.Empty).Trim().ToLowerInvariant());
}
