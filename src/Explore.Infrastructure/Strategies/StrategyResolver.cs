// Uses module service to check which strategies are available for a tenant.

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Strategies;
using Explore.Application.DTOs.Event;
using Explore.Domain;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Strategies;

/// <summary>
/// Resolves and orchestrates event strategies based on tenant capabilities.
/// </summary>
public class StrategyResolver : IStrategyResolver
{
    private readonly IEnumerable<IEventStrategy> _strategies;
    private readonly IModuleService _moduleService;
    private readonly ILogger<StrategyResolver> _logger;

    public StrategyResolver(
        IEnumerable<IEventStrategy> strategies,
        IModuleService moduleService,
        ILogger<StrategyResolver> logger)
    {
        _strategies = strategies;
        _moduleService = moduleService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<IEventStrategy>> GetApplicableStrategiesAsync(
        Guid tenantId,
        CreateEventDto dto,
        CancellationToken cancellationToken = default)
    {
        var applicableStrategies = new List<IEventStrategy>();

        foreach (var strategy in _strategies)
        {
            // Check if module is enabled for tenant
            var isModuleEnabled = await _moduleService.IsModuleEnabledAsync(
                tenantId, strategy.ModuleKey, cancellationToken);

            if (!isModuleEnabled)
            {
                _logger.LogDebug(
                    "Strategy skipped because its module is disabled");
                continue;
            }

            // Check if strategy is applicable to the request
            if (strategy.IsApplicable(dto))
            {
                applicableStrategies.Add(strategy);
                _logger.LogDebug(
                    "Strategy is applicable for event");
            }
        }

        // Order by priority (lower = higher priority)
        return applicableStrategies
            .OrderBy(s => s.Priority)
            .ToList();
    }

    public async Task<ValidationResult> ValidateWithStrategiesAsync(
        Guid tenantId,
        CreateEventDto dto,
        CancellationToken cancellationToken = default)
    {
        var result = new ValidationResult();
        var strategies = await GetApplicableStrategiesAsync(tenantId, dto, cancellationToken);

        foreach (var strategy in strategies)
        {
            try
            {
                var strategyResult = await strategy.ValidateAsync(dto, cancellationToken);
                if (!strategyResult.IsValid)
                {
                    result.Errors.AddRange(strategyResult.Errors);
                    _logger.LogDebug(
                        "Strategy validation failed with {ErrorCount} errors",
                        strategyResult.Errors.Count);
                }
            }
            catch (Exception)
            {
                _logger.LogError("Strategy validation failed unexpectedly");

                result.Errors.Add(new ValidationFailure(
                    "strategy",
                    "Strategy validation is unavailable."));
            }
        }

        return result;
    }

    public async Task ExecutePostCreateAsync(
        Guid tenantId,
        Event @event,
        CreateEventDto dto,
        CancellationToken cancellationToken = default)
    {
        var strategies = await GetApplicableStrategiesAsync(tenantId, dto, cancellationToken);

        foreach (var strategy in strategies)
        {
            try
            {
                await strategy.PostCreateAsync(@event, cancellationToken);
                _logger.LogDebug(
                    "Strategy post-create completed");
            }
            catch (Exception)
            {
                // Log but don't fail the operation - post-create is non-critical
                _logger.LogError("Strategy post-create failed");
            }
        }
    }

    public async Task ExecutePostUpdateAsync(
        Guid tenantId,
        Event @event,
        CancellationToken cancellationToken = default)
    {
        // For post-update, we check which strategies are applicable based on the event's aspects
        var enabledModules = await _moduleService.GetEnabledModulesAsync(tenantId, cancellationToken);
        var enabledModuleKeys = enabledModules.Select(m => m.ModuleKey).ToHashSet();

        foreach (var strategy in _strategies.Where(s => enabledModuleKeys.Contains(s.ModuleKey)))
        {
            // Check if event has data for this strategy
            var isApplicable = strategy.ModuleKey switch
            {
                "Mod_Islamic" => @event.IslamicAspect != null,
                "Mod_Tech" => @event.TechAspect != null,
                _ => false
            };

            if (!isApplicable) continue;

            try
            {
                await strategy.PostUpdateAsync(@event, cancellationToken);
                _logger.LogDebug(
                    "Strategy post-update completed");
            }
            catch (Exception)
            {
                _logger.LogError("Strategy post-update failed");
            }
        }
    }

    public async Task<IReadOnlyList<StrategyLink>> GetStrategyLinksAsync(
        Guid tenantId,
        Event @event,
        CancellationToken cancellationToken = default)
    {
        var links = new List<StrategyLink>();
        var enabledModules = await _moduleService.GetEnabledModulesAsync(tenantId, cancellationToken);
        var enabledModuleKeys = enabledModules.Select(m => m.ModuleKey).ToHashSet();

        foreach (var strategy in _strategies.Where(s => enabledModuleKeys.Contains(s.ModuleKey)))
        {
            try
            {
                var strategyLinks = strategy.GetLinks(@event);
                links.AddRange(strategyLinks);
            }
            catch (Exception)
            {
                _logger.LogError("Strategy link discovery failed");
            }
        }

        return links;
    }
}
