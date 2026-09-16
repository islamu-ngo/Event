using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventTicketing.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventTicketing.Handlers.Commands;

public sealed class DeleteEventCapacityPoolCommandHandler(
    IEventRepository events,
    IEventTicketCatalogRepository catalogs,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork,
    HybridCache cache) : ICommandHandler<DeleteEventCapacityPoolCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        DeleteEventCapacityPoolCommand command,
        CancellationToken cancellationToken)
    {
        Event? eventTarget = await events.GetAuthorizationTargetByIdAsync(command.EventId, cancellationToken);
        if (!IsPlatformManaged(eventTarget, tenant.TenantId))
        {
            return Missing(command.CapacityPoolId);
        }

        if (currentUser.UserId is not Guid userId)
        {
            return Bad(command.CapacityPoolId, "An authenticated user is required.");
        }

        try
        {
            BaseCommandResponse<Guid> response = await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                EventCapacityPool? pool = await catalogs.GetActiveCapacityPoolForUpdateAsync(
                    command.CapacityPoolId,
                    command.EventId,
                    tenant.TenantId,
                    token);
                if (pool is null)
                {
                    return Missing(command.CapacityPoolId);
                }

                bool hasLiveReferences = await catalogs.HasLiveTicketTypeReferencesAsync(
                    pool.Id,
                    command.EventId,
                    tenant.TenantId,
                    token);
                if (hasLiveReferences)
                {
                    return Bad(pool.Id, "Capacity pool is assigned to an active ticket type.");
                }

                pool.Delete(timeProvider.GetUtcNow().UtcDateTime, userId);
                await catalogs.UpdateCapacityPoolAsync(pool, token);
                return Ok(pool.Id, "Capacity pool deleted.");
            }, cancellationToken);

            if (!response.IsSuccess)
            {
                return response;
            }

            await cache.RemoveAsync($"event:detail:{command.EventId}", cancellationToken);
            return response;
        }
        catch (ArgumentException exception)
        {
            return Bad(command.CapacityPoolId, exception.Message);
        }
    }

    private static bool IsPlatformManaged(Event? eventTarget, Guid tenantId) =>
        eventTarget?.TenantId == tenantId
        && eventTarget.ParticipationConfiguration?.ParticipationHandlingModeId
            == (int)ParticipationHandlingModeEnum.PlatformManaged;

    private static BaseCommandResponse<Guid> Ok(Guid id, string message) => BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Missing(Guid id) => BaseCommandResponse.Failure<Guid>(
        "event_ticketing_not_found", "Ticketing configuration was not found.", ["Ticketing configuration was not found."], id);

    private static BaseCommandResponse<Guid> Bad(Guid id, string error) => BaseCommandResponse.Failure<Guid>(
        "event_ticketing_validation_failed", "Ticketing configuration is invalid.", [error], id);
}
