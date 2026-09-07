// for Madhab, prayer times, and gender segregation features.

using Explore.Application.Contracts.Strategies;
using Explore.Application.DTOs.Event;
using Explore.Domain;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Strategies;

/// <summary>
/// Strategy for Islamic event features: Madhab selection, prayer time scheduling, gender segregation.
/// </summary>
public class IslamicEventStrategy : IEventStrategy
{
    private readonly ILogger<IslamicEventStrategy> _logger;

    public IslamicEventStrategy(ILogger<IslamicEventStrategy> logger)
    {
        _logger = logger;
    }

    public string ModuleKey => "Mod_Islamic";

    public int Priority => 10; // High priority for domain-specific logic

    public bool IsApplicable(CreateEventDto dto)
    {
        // Islamic strategy applies when MadhabId is specified (Islamic context)
        return dto.MadhabId.HasValue;
    }

    public Task<ValidationResult> ValidateAsync(CreateEventDto dto, CancellationToken cancellationToken = default)
    {
        var result = new ValidationResult();

        // MadhabId validation is handled by the standard validator
        // Additional Islamic-specific validation can be added here if needed

        return Task.FromResult(result);
    }

    public Task PostCreateAsync(Event @event, CancellationToken cancellationToken = default)
    {
        // Log Islamic event creation with aspect details
        if (@event.IslamicAspect != null)
        {
            _logger.LogInformation(
                "Islamic event {EventId} created with Madhab {MadhabId}, Prayer: {Prayer}, Gender Mode: {GenderMode}",
                @event.Id,
                @event.IslamicAspect.MadhabId,
                @event.IslamicAspect.ReferencePrayer,
                @event.IslamicAspect.GenderMode);

            // Future enhancement: Could trigger a background job to:
            // 1. Lookup prayer times for the event location
            // 2. Calculate actual session start times
            // 3. Update session schedules accordingly
        }

        return Task.CompletedTask;
    }

    public Task PostUpdateAsync(Event @event, CancellationToken cancellationToken = default)
    {
        // Same logic as post-create for prayer time recalculation
        return PostCreateAsync(@event, cancellationToken);
    }

    public IEnumerable<StrategyLink> GetLinks(Event @event)
    {
        if (@event.IslamicAspect == null)
            yield break;

        yield return new StrategyLink
        {
            Rel = "islamic-aspect",
            Href = $"/api/events/{@event.Id}/aspects/islamic",
            Method = "GET",
            Title = "Islamic event details"
        };

        if (@event.IslamicAspect.MadhabId.HasValue)
        {
            yield return new StrategyLink
            {
                Rel = "madhab",
                Href = $"/api/madhabs/{@event.IslamicAspect.MadhabId}",
                Method = "GET",
                Title = "Madhab information"
            };
        }
    }
}
