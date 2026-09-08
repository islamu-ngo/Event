// Orchestrates strategy selection and validation across modules.

using Explore.Application.DTOs.Event;
using Explore.Domain;
using FluentValidation.Results;

namespace Explore.Application.Contracts.Strategies;

/// <summary>
/// Resolves and orchestrates event strategies based on tenant capabilities.
/// </summary>
public interface IStrategyResolver
{
    /// <summary>
    /// Gets all strategies applicable to the given DTO for the current tenant.
    /// </summary>
    /// <param name="tenantId">The tenant to check capabilities for.</param>
    /// <param name="dto">The DTO to match against strategies.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of applicable strategies ordered by priority.</returns>
    Task<IReadOnlyList<IEventStrategy>> GetApplicableStrategiesAsync(
        Guid tenantId,
        CreateEventDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the DTO using all applicable strategies.
    /// </summary>
    /// <param name="tenantId">The tenant to check capabilities for.</param>
    /// <param name="dto">The DTO to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Combined validation result from all strategies.</returns>
    Task<ValidationResult> ValidateWithStrategiesAsync(
        Guid tenantId,
        CreateEventDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes post-create logic for all applicable strategies.
    /// </summary>
    /// <param name="tenantId">The tenant to check capabilities for.</param>
    /// <param name="event">The created event.</param>
    /// <param name="dto">The original DTO (for strategy matching).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ExecutePostCreateAsync(
        Guid tenantId,
        Event @event,
        CreateEventDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes post-update logic for all applicable strategies.
    /// </summary>
    /// <param name="tenantId">The tenant to check capabilities for.</param>
    /// <param name="event">The updated event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ExecutePostUpdateAsync(
        Guid tenantId,
        Event @event,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all HATEOAS links from applicable strategies for an event.
    /// </summary>
    /// <param name="tenantId">The tenant to check capabilities for.</param>
    /// <param name="event">The event to generate links for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of strategy links.</returns>
    Task<IReadOnlyList<StrategyLink>> GetStrategyLinksAsync(
        Guid tenantId,
        Event @event,
        CancellationToken cancellationToken = default);
}
