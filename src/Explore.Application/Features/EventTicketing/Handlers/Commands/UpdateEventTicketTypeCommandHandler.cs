using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventTicketing.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventTicketing.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventTicketing.Handlers.Commands;

public sealed class UpdateEventTicketTypeCommandHandler(
    IEventRepository events,
    IEventTicketCatalogRepository catalogs,
    TicketTypeEntitlementResolver entitlementResolver,
    ITenantContext tenant,
    IUnitOfWork unitOfWork,
    HybridCache cache) : ICommandHandler<UpdateEventTicketTypeCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        UpdateEventTicketTypeCommand command,
        CancellationToken cancellationToken)
    {
        var validation = await new ManageEventTicketTypeDtoValidator()
            .ValidateAsync(command.TicketType, cancellationToken);
        if (!validation.IsValid)
        {
            return Bad(command.TicketTypeId, validation.Errors.Select(error => error.ErrorMessage));
        }

        Event? eventTarget = await events.GetAuthorizationTargetByIdAsync(command.EventId, cancellationToken);
        if (!IsPlatformManaged(eventTarget, tenant.TenantId))
        {
            return Missing(command.TicketTypeId);
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

                EventCapacityPool? pool = command.TicketType.CapacityPoolId.HasValue
                    ? await catalogs.GetActiveCapacityPoolForUpdateAsync(
                        command.TicketType.CapacityPoolId.Value,
                        command.EventId,
                        tenant.TenantId,
                        token)
                    : null;
                if (command.TicketType.CapacityPoolId.HasValue && pool is null)
                {
                    return null;
                }

                pool?.RegisterTicketAssignment();

                IReadOnlyList<TicketTypeEntitlement> entitlements = await entitlementResolver.ResolveAsync(
                    ticketType.Id,
                    command.TicketType.Entitlements,
                    command.EventId,
                    token);

                TicketTypeEntitlement[] existingEntitlements = ticketType.Entitlements.ToArray();
                await catalogs.RemoveEntitlementsAsync(existingEntitlements, token);
                catalog!.UpdateTicketType(
                    ticketType,
                    command.TicketType.Name,
                    (TicketPricingModeEnum)command.TicketType.TicketPricingModeId,
                    CreateMoney(command.TicketType.FixedPriceMinor, catalog.CurrencyCode),
                    CreateMoney(command.TicketType.MinimumPriceMinor, catalog.CurrencyCode),
                    CreateMoney(command.TicketType.SuggestedPriceMinor, catalog.CurrencyCode),
                    (ParticipantDataCollectionModeEnum)command.TicketType.ParticipantDataCollectionModeId,
                    pool,
                    command.TicketType.MinimumAge,
                    command.TicketType.MaximumAge,
                    command.TicketType.RequiresGuardian,
                    command.TicketType.RequiresApproval,
                    command.TicketType.PerOrderLimit,
                    command.TicketType.PerAccountLimit,
                    command.TicketType.PerVerifiedContactLimit,
                    command.TicketType.PerBookingPartyLimit,
                    entitlements);
                await catalogs.UpdateAsync(catalog, token);
                return ticketType.Id;
            }, cancellationToken);

            if (ticketTypeId is null)
            {
                return Missing(command.TicketTypeId);
            }

            await cache.RemoveAsync($"event:detail:{command.EventId}", cancellationToken);
            return Ok(ticketTypeId.Value, "Ticket type updated.");
        }
        catch (TicketingNotFoundException)
        {
            return Missing(command.TicketTypeId);
        }
        catch (ConcurrencyConflictException exception)
        {
            return Conflict(command.TicketTypeId, exception.Message);
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

    private static Money? CreateMoney(long? minorUnits, string currencyCode) =>
        minorUnits.HasValue ? Money.Create(minorUnits.Value, currencyCode) : null;

    private static BaseCommandResponse<Guid> Ok(Guid id, string message) => BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Missing(Guid id) => BaseCommandResponse.Failure<Guid>(
        "event_ticketing_not_found", "Ticketing configuration was not found.", ["Ticketing configuration was not found."], id);

    private static BaseCommandResponse<Guid> Bad(Guid id, string error) => Bad(id, [error]);

    private static BaseCommandResponse<Guid> Bad(Guid id, IEnumerable<string> errors) => BaseCommandResponse.Failure<Guid>(
        "event_ticketing_validation_failed", "Ticketing configuration is invalid.", errors, id);

    private static BaseCommandResponse<Guid> Conflict(Guid id, string error) => BaseCommandResponse.Failure<Guid>(
        "event_ticketing_concurrency_conflict", "Ticketing configuration was updated by another request.", [error], id);
}
