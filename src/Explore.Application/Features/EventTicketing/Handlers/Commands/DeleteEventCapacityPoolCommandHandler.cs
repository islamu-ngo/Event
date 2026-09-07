using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventTicketing.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventTicketing.Handlers.Commands;

public sealed class DeleteEventCapacityPoolCommandHandler(
    IEventRepository events,
    IEventTicketCatalogRepository catalogs,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork,
    HybridCache cache) : IRequestHandler<DeleteEventCapacityPoolCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        DeleteEventCapacityPoolCommand request,
        CancellationToken cancellationToken)
    {
        Event? eventTarget = await events.GetAuthorizationTargetByIdAsync(request.EventId, cancellationToken);
        if (!IsPlatformManaged(eventTarget, tenant.TenantId))
        {
            return Missing(request.CapacityPoolId);
        }

        if (currentUser.UserId is not Guid userId)
        {
            return Bad(request.CapacityPoolId, "An authenticated user is required.");
        }

        try
        {
            BaseCommandResponse<Guid> response = await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                EventCapacityPool? pool = await catalogs.GetActiveCapacityPoolForUpdateAsync(
                    request.CapacityPoolId,
                    request.EventId,
                    tenant.TenantId,
                    token);
                if (pool is null)
                {
                    return Missing(request.CapacityPoolId);
                }

                bool hasLiveReferences = await catalogs.HasLiveTicketTypeReferencesAsync(
                    pool.Id,
                    request.EventId,
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

            await cache.RemoveAsync($"event:detail:{request.EventId}", cancellationToken);
            return response;
        }
        catch (ArgumentException exception)
        {
            return Bad(request.CapacityPoolId, exception.Message);
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
