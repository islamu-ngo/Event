using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventTicketing.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventTicketing.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventTicketing.Handlers.Commands;

public sealed class CreateEventCapacityPoolCommandHandler(
    IEventRepository events,
    IEventTicketCatalogRepository catalogs,
    ITenantContext tenant,
    HybridCache cache) : ICommandHandler<CreateEventCapacityPoolCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        CreateEventCapacityPoolCommand command,
        CancellationToken cancellationToken)
    {
        var validation = await new ManageEventCapacityPoolDtoValidator()
            .ValidateAsync(command.CapacityPool, cancellationToken);
        if (!validation.IsValid)
        {
            return Bad(command.EventId, validation.Errors.Select(error => error.ErrorMessage));
        }

        Event? eventTarget = await events.GetAuthorizationTargetByIdAsync(command.EventId, cancellationToken);
        if (!IsPlatformManaged(eventTarget, tenant.TenantId))
        {
            return Missing(command.EventId);
        }

        try
        {
            EventCapacityPool pool = EventCapacityPool.Create(
                tenant.TenantId,
                command.EventId,
                command.CapacityPool.Name,
                command.CapacityPool.MaximumQuantity,
                command.CapacityPool.HoldDurationSeconds,
                (CapacityHoldPolicyEnum)command.CapacityPool.CapacityHoldPolicyId,
                (CapacityOversellPolicyEnum)command.CapacityPool.CapacityOversellPolicyId,
                command.CapacityPool.IsActive);
            await catalogs.AddCapacityPoolAsync(pool, cancellationToken);
            await cache.RemoveAsync($"event:detail:{command.EventId}", cancellationToken);
            return Ok(pool.Id, "Capacity pool created.");
        }
        catch (ConcurrencyConflictException exception)
        {
            return Conflict(command.EventId, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Bad(command.EventId, exception.Message);
        }
    }

    private static bool IsPlatformManaged(Event? eventTarget, Guid tenantId) =>
        eventTarget?.TenantId == tenantId
        && eventTarget.ParticipationConfiguration?.ParticipationHandlingModeId
            == (int)ParticipationHandlingModeEnum.PlatformManaged;

    private static BaseCommandResponse<Guid> Ok(Guid id, string message) => BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Missing(Guid id) => BaseCommandResponse.Failure<Guid>(
        "event_ticketing_not_found", "Ticketing configuration was not found.", ["Ticketing configuration was not found."], id);

    private static BaseCommandResponse<Guid> Bad(Guid id, string error) => Bad(id, [error]);

    private static BaseCommandResponse<Guid> Bad(Guid id, IEnumerable<string> errors) => BaseCommandResponse.Failure<Guid>(
        "event_ticketing_validation_failed", "Ticketing configuration is invalid.", errors, id);

    private static BaseCommandResponse<Guid> Conflict(Guid id, string error) => BaseCommandResponse.Failure<Guid>(
        "event_ticketing_concurrency_conflict", "Ticketing configuration was updated by another request.", [error], id);
}
