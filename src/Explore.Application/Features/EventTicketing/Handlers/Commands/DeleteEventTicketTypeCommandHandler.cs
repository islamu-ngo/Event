using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventTicketing.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventTicketing.Handlers.Commands;

public sealed class DeleteEventTicketTypeCommandHandler(
    IEventRepository events,
    IEventTicketCatalogRepository catalogs,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork,
    HybridCache cache) : ICommandHandler<DeleteEventTicketTypeCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        DeleteEventTicketTypeCommand command,
        CancellationToken cancellationToken)
    {
        Event? eventTarget = await events.GetAuthorizationTargetByIdAsync(command.EventId, cancellationToken);
        if (!IsPlatformManaged(eventTarget, tenant.TenantId))
        {
            return Missing(command.TicketTypeId);
        }

        if (currentUser.UserId is not Guid userId)
        {
            return Bad(command.TicketTypeId, "An authenticated user is required.");
        }

        try
        {
            Guid? ticketTypeId = await unitOfWork.ExecuteInTransactionAsync<Guid?>(async token =>
            {
                EventTicketCatalogVersion? catalog = await catalogs.GetDraftCatalogForUpdateAsync(
                    command.EventId,
                    tenant.TenantId,
                    token);
                EventTicketType? ticketType = catalog?.TicketTypes.SingleOrDefault(
                    candidate => candidate.Id == command.TicketTypeId && !candidate.IsDeleted);
                if (ticketType is null)
                {
                    return null;
                }

                catalog!.DeleteTicketType(ticketType, timeProvider.GetUtcNow().UtcDateTime, userId);
                await catalogs.UpdateAsync(catalog, token);
                return ticketType.Id;
            }, cancellationToken);

            if (ticketTypeId is null)
            {
                return Missing(command.TicketTypeId);
            }

            await cache.RemoveAsync($"event:detail:{command.EventId}", cancellationToken);
            return Ok(ticketTypeId.Value, "Ticket type deleted.");
        }
        catch (ArgumentException exception)
        {
            return Bad(command.TicketTypeId, exception.Message);
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
