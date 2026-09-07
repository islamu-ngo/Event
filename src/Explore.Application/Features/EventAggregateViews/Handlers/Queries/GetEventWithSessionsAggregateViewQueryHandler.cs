using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventAggregateViews.Requests.Queries;
using Explore.Application.Responses;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventAggregateViews.Handlers.Queries;

public sealed class GetEventWithSessionsAggregateViewQueryHandler
    : IRequestHandler<GetEventWithSessionsAggregateViewQuery, BaseCommandResponse<DTOs.EventAggregateView.EventWithSessionsViewDto>>
{
    private readonly IEventAggregateViewRepository _repository;
    private readonly HybridCache _cache;
    private readonly ILogger<GetEventWithSessionsAggregateViewQueryHandler> _logger;

    public GetEventWithSessionsAggregateViewQueryHandler(
        IEventAggregateViewRepository repository,
        HybridCache cache,
        ILogger<GetEventWithSessionsAggregateViewQueryHandler> logger)
    {
        _repository = repository;
        _cache = cache;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<DTOs.EventAggregateView.EventWithSessionsViewDto>> Handle(
        GetEventWithSessionsAggregateViewQuery request,
        CancellationToken cancellationToken)
    {
        var validator = new GetEventWithSessionsAggregateViewQueryValidator();
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        var cacheKey = $"event-aggregate:detail:{request.EventId}:{request.ExposureCeiling}";

        return await _cache.GetOrCreateAsync(
            cacheKey,
            async _ =>
            {
                var view = await _repository.GetByEventIdAsync(request.EventId, cancellationToken);
                if (view is null)
                {
                    return BaseCommandResponse.Validation<DTOs.EventAggregateView.EventWithSessionsViewDto>(
                        ["Event aggregate view was not found."],
                        "Event aggregate view was not found.");
                }

                var eventDefinitions = await _repository.GetEventDefinitionsByEventIdsAsync([request.EventId], cancellationToken);
                var sessionDefinitions = await _repository.GetSessionDefinitionsForEventAsync(request.EventId, cancellationToken);

                var dto = EventAggregateViewMapper.MapDetail(view, eventDefinitions, sessionDefinitions, request.ExposureCeiling, _logger);

                return BaseCommandResponse.Success(
                    dto,
                    "Event aggregate view retrieved successfully.");
            },
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(1)
            },
            cancellationToken: cancellationToken);
    }
}
