// for GitHub repositories, skill levels, and hackathon features.

using Explore.Application.Contracts.Strategies;
using Explore.Application.DTOs.Event;
using Explore.Domain;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Strategies;

/// <summary>
/// Strategy for tech event features: GitHub repos, skill levels, hackathons.
/// </summary>
public class TechEventStrategy : IEventStrategy
{
    private readonly ILogger<TechEventStrategy> _logger;

    public TechEventStrategy(ILogger<TechEventStrategy> logger)
    {
        _logger = logger;
    }

    public string ModuleKey => "Mod_Tech";

    public int Priority => 10; // Same priority as Islamic for domain modules

    public bool IsApplicable(CreateEventDto dto)
    {
        // Tech strategy applies when the event has tech-related characteristics
        // For now, check if EventType suggests a tech event (can be expanded)
        // This is a simplified check - full implementation would check event type
        return false; // Will be enabled when TechAspect is added to CreateEventDto
    }

    public Task<ValidationResult> ValidateAsync(CreateEventDto dto, CancellationToken cancellationToken = default)
    {
        var result = new ValidationResult();

        // Tech-specific validation will be added when TechAspect is in CreateEventDto
        // For now, return empty result

        return Task.FromResult(result);
    }

    public Task PostCreateAsync(Event @event, CancellationToken cancellationToken = default)
    {
        if (@event.TechAspect != null)
        {
            _logger.LogInformation(
                "Tech event {EventId} created. Skill Level: {SkillLevel}, Requires Laptop: {RequiresLaptop}",
                @event.Id,
                @event.TechAspect.SkillLevel,
                @event.TechAspect.RequiresLaptop);

            // Future enhancement: Could trigger:
            // 1. GitHub webhook setup for repo updates
            // 2. Prerequisites email to registrants
        }

        return Task.CompletedTask;
    }

    public Task PostUpdateAsync(Event @event, CancellationToken cancellationToken = default)
    {
        // Log updates for tech events
        if (@event.TechAspect != null)
        {
            _logger.LogInformation("Tech event {EventId} updated", @event.Id);
        }

        return Task.CompletedTask;
    }

    public IEnumerable<StrategyLink> GetLinks(Event @event)
    {
        if (@event.TechAspect == null)
            yield break;

        yield return new StrategyLink
        {
            Rel = "tech-aspect",
            Href = $"/api/events/{@event.Id}/aspects/tech",
            Method = "GET",
            Title = "Tech event details"
        };

        if (!string.IsNullOrEmpty(@event.TechAspect.GithubRepoUrl))
        {
            yield return new StrategyLink
            {
                Rel = "github-repo",
                Href = @event.TechAspect.GithubRepoUrl,
                Method = "GET",
                Title = "GitHub repository"
            };
        }
    }
}
