using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventTicketing.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventTicketing.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventTicketing.Handlers.Commands;

public sealed class UpdateEventTicketTypeCommandHandler(
    IEventRepository events,
    IEventTicketCatalogRepository catalogs,
    TicketTypeEntitlementResolver entitlementResolver,
    ITenantContext tenant,
    IUnitOfWork unitOfWork,
    HybridCache cache) : IRequestHandler<UpdateEventTicketTypeCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        UpdateEventTicketTypeCommand request,
        CancellationToken cancellationToken)
    {
        var validation = await new ManageEventTicketTypeDtoValidator()
            .ValidateAsync(request.TicketType, cancellationToken);
        if (!validation.IsValid)
        {
            return Bad(request.TicketTypeId, validation.Errors.Select(error => error.ErrorMessage));
        }

        Event? eventTarget = await events.GetAuthorizationTargetByIdAsync(request.EventId, cancellationToken);
        if (!IsPlatformManaged(eventTarget, tenant.TenantId))
        {
            return Missing(request.TicketTypeId);
        }

        try
        {
            Guid? ticketTypeId = await unitOfWork.ExecuteInTransactionAsync<Guid?>(async token =>
            {
                EventTicketCatalogVersion? catalog = await catalogs.GetDraftCatalogForUpdateAsync(
                    request.EventId,
                    tenant.TenantId,
                    token);
                EventTicketType? ticketType = catalog?.TicketTypes.SingleOrDefault(
                    candidate => candidate.Id == request.TicketTypeId && !candidate.IsDeleted);
                if (ticketType is null)
                {
                    return null;
                }

                EventCapacityPool? pool = request.TicketType.CapacityPoolId.HasValue
                    ? await catalogs.GetActiveCapacityPoolForUpdateAsync(
                        request.TicketType.CapacityPoolId.Value,
                        request.EventId,
                        tenant.TenantId,
                        token)
                    : null;
                if (request.TicketType.CapacityPoolId.HasValue && pool is null)
                {
                    return null;
                }

                pool?.RegisterTicketAssignment();

                IReadOnlyList<TicketTypeEntitlement> entitlements = await entitlementResolver.ResolveAsync(
                    ticketType.Id,
                    request.TicketType.Entitlements,
                    request.EventId,
                    token);

                TicketTypeEntitlement[] existingEntitlements = ticketType.Entitlements.ToArray();
                await catalogs.RemoveEntitlementsAsync(existingEntitlements, token);
                catalog!.UpdateTicketType(
                    ticketType,
                    request.TicketType.Name,
                    (TicketPricingModeEnum)request.TicketType.TicketPricingModeId,
                    CreateMoney(request.TicketType.FixedPriceMinor, catalog.CurrencyCode),
                    CreateMoney(request.TicketType.MinimumPriceMinor, catalog.CurrencyCode),
                    CreateMoney(request.TicketType.SuggestedPriceMinor, catalog.CurrencyCode),
                    (ParticipantDataCollectionModeEnum)request.TicketType.ParticipantDataCollectionModeId,
                    pool,
                    request.TicketType.MinimumAge,
                    request.TicketType.MaximumAge,
                    request.TicketType.RequiresGuardian,
                    request.TicketType.RequiresApproval,
                    request.TicketType.PerOrderLimit,
                    request.TicketType.PerAccountLimit,
                    request.TicketType.PerVerifiedContactLimit,
                    request.TicketType.PerBookingPartyLimit,
                    entitlements);
                await catalogs.UpdateAsync(catalog, token);
                return ticketType.Id;
            }, cancellationToken);

            if (ticketTypeId is null)
            {
                return Missing(request.TicketTypeId);
            }

            await cache.RemoveAsync($"event:detail:{request.EventId}", cancellationToken);
            return Ok(ticketTypeId.Value, "Ticket type updated.");
        }
        catch (TicketingNotFoundException)
        {
            return Missing(request.TicketTypeId);
        }
        catch (ConcurrencyConflictException exception)
        {
            return Conflict(request.TicketTypeId, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Bad(request.TicketTypeId, exception.Message);
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
