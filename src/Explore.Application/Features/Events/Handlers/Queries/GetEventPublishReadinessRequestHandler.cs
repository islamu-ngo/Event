using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Services.Lifecycle;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Events.Handlers.Queries;

public class GetEventPublishReadinessRequestHandler(
    IEventRepository eventRepository,
    IEventLocationRepository eventLocationRepository,
    IEventLifecyclePolicyProvider policyProvider,
    IEventLifecycleReadinessEvaluator readinessEvaluator)
    : IRequestHandler<GetEventPublishReadinessRequest, EventPublishReadinessDto?>
{
    public async Task<EventPublishReadinessDto?> Handle(GetEventPublishReadinessRequest request, CancellationToken cancellationToken)
    {
        var @event = await eventRepository.GetById(request.Id);
        if (@event is null)
        {
            return null;
        }

        EventLifecyclePolicy policy = await policyProvider.GetEffectivePolicyAsync(@event.TenantId, ValidationProfile.EventPublish, cancellationToken);
        LifecycleReadinessResult result = readinessEvaluator.Evaluate(@event, policy.Profile, policy);
        IReadOnlyList<EventLocation> eventLocations = await eventLocationRepository.GetByEventIdAsync(
            @event.Id,
            cancellationToken);
        result = EventLocationPublicationReadinessEvaluator.Include(result, eventLocations);

        return MapToDto(@event.Id, result);
    }

    /// <summary>
    /// Maps the internal <see cref="LifecycleReadinessResult"/> to the API-facing
    /// <see cref="EventPublishReadinessDto"/> to preserve the wire contract while
    /// using the rich machine-readable error model internally.
    /// </summary>
    private static EventPublishReadinessDto MapToDto(Guid eventId, LifecycleReadinessResult result)
    {
        return new EventPublishReadinessDto
        {
            EventId = eventId,
            IsReady = result.IsReady,
            Errors = result.Errors
                .Select(error => new EventPublishReadinessErrorDto
                {
                    Code = error.Code,
                    FieldPath = error.FieldPath,
                    Message = error.Message,
                    Severity = error.Severity.ToString().ToLowerInvariant()
                })
                .ToList()
        };
    }
}
