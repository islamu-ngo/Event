using Explore.Application.Authorization;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventTicketing;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventTicketing.Requests.Commands;
using Explore.Application.Features.EventTicketing.Services;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventTicketing.Handlers.Commands;

public sealed class PublishEventTicketCatalogCommandHandler(
    IEventRepository events,
    IEventTicketCatalogRepository catalogs,
    IEventDayRepository eventDays,
    IEventSessionRepository eventSessions,
    IAdmissionTargetMaterializer admissionTargets,
    ITenantContext tenant,
    IUnitOfWork unitOfWork,
    PaidEventPublicationPreflightService paidPreflight,
    HybridCache cache) : IRequestHandler<PublishEventTicketCatalogCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        PublishEventTicketCatalogCommand request,
        CancellationToken cancellationToken)
    {
        Event? eventTarget = await events.GetAuthorizationTargetByIdAsync(request.EventId, cancellationToken);
        if (!IsPlatformManaged(eventTarget, tenant.TenantId))
        {
            return Missing(request.EventId);
        }

        Guid failureId = request.EventId;
        Guid? catalogId;
        try
        {
            catalogId = await unitOfWork.ExecuteInTransactionAsync<Guid?>(async token =>
            {
                Event? trustedEventTarget = await events.GetEventWithDetails(request.EventId);
                if (!IsPlatformManaged(trustedEventTarget, tenant.TenantId))
                {
                    return null;
                }

                EventTicketCatalogVersion? draft = await catalogs.GetDraftCatalogForUpdateAsync(
                    request.EventId,
                    tenant.TenantId,
                    token);
                if (draft is null)
                {
                    return null;
                }

                failureId = draft.Id;
                EventTicketCatalogVersion? currentPublication = await catalogs.GetPublishedForUpdateAsync(
                    request.EventId,
                    tenant.TenantId,
                    token);

                await ValidateEntitlementTargetsAsync(draft, token);

                PaidEventPublicationPreflightDto preflight = await paidPreflight.AssessAsync(request.EventId, trustedEventTarget, draft, token);
                if (preflight.IsPaidCatalog && !preflight.IsReady)
                {
                    if (preflight.Blockers.Any(blocker => blocker.Code == "commerce_authorization_denied"))
                    {
                        throw new AuthorizationException(ResourceKinds.Event, AuthorizationActions.Events.ManagePaidEventCommerce);
                    }

                    throw new ArgumentException(string.Join(" ", preflight.Blockers.Select(blocker => blocker.Explanation)));
                }

                try
                {
                    draft.ValidateForPublication();
                    await admissionTargets.MaterializeAsync(trustedEventTarget, draft, token);
                    currentPublication?.Retire();
                }
                catch (InvalidOperationException exception)
                {
                    throw new ArgumentException(exception.Message, exception);
                }

                await catalogs.SaveChangesAsync(token);

                try
                {
                    draft.Publish();
                }
                catch (InvalidOperationException exception)
                {
                    throw new ArgumentException(exception.Message, exception);
                }

                await catalogs.SaveChangesAsync(token);
                return draft.Id;
            }, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return Bad(failureId, exception.Message);
        }
        catch (ConcurrencyConflictException exception)
        {
            return Conflict(failureId, exception.Message);
        }

        if (catalogId is null)
        {
            return Missing(request.EventId);
        }

        await cache.RemoveAsync($"event:detail:{request.EventId}", cancellationToken);
        return Ok(catalogId.Value, "Ticket catalog published.");
    }

    private async Task ValidateEntitlementTargetsAsync(
        EventTicketCatalogVersion draft,
        CancellationToken cancellationToken)
    {
        TicketTypeEntitlement[] entitlements = draft.TicketTypes
            .Where(ticketType => !ticketType.IsDeleted)
            .SelectMany(ticketType => ticketType.Entitlements)
            .ToArray();

        foreach (Guid eventDayId in entitlements
                     .Where(entitlement => entitlement.EventDayId.HasValue)
                     .Select(entitlement => entitlement.EventDayId!.Value)
                     .Distinct()
                     .Order())
        {
            EventDay? eventDay = await eventDays.GetByIdForEventForUpdateAsync(
                eventDayId,
                draft.EventId,
                draft.TenantId,
                cancellationToken);
            EnsureActiveTarget(eventDay, draft.EventId, draft.TenantId);
        }

        foreach (Guid eventSessionId in entitlements
                     .Where(entitlement => entitlement.EventSessionId.HasValue)
                     .Select(entitlement => entitlement.EventSessionId!.Value)
                     .Distinct()
                     .Order())
        {
            EventSession? eventSession = await eventSessions.GetByIdForEventForUpdateAsync(
                eventSessionId,
                draft.EventId,
                draft.TenantId,
                cancellationToken);
            EnsureActiveTarget(eventSession, draft.EventId, draft.TenantId);
        }
    }

    private static void EnsureActiveTarget(EventDay? target, Guid eventId, Guid tenantId)
    {
        if (target is null || target.IsDeleted || target.EventId != eventId || target.TenantId != tenantId)
        {
            throw new ArgumentException("Ticket entitlement targets must be active and belong to the catalog event and tenant.");
        }
    }

    private static void EnsureActiveTarget(EventSession? target, Guid eventId, Guid tenantId)
    {
        if (target is null || target.IsDeleted || target.EventId != eventId || target.TenantId != tenantId)
        {
            throw new ArgumentException("Ticket entitlement targets must be active and belong to the catalog event and tenant.");
        }
    }

    private static bool IsPlatformManaged(Event? eventTarget, Guid tenantId) =>
        eventTarget?.TenantId == tenantId
        && eventTarget.ParticipationConfiguration?.ParticipationHandlingModeId == (int)ParticipationHandlingModeEnum.PlatformManaged;

    private static BaseCommandResponse<Guid> Ok(Guid id, string message) => BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Missing(Guid id) => BaseCommandResponse.Failure<Guid>(
        "event_ticketing_not_found", "Ticketing configuration was not found.", ["Ticketing configuration was not found."], id);

    private static BaseCommandResponse<Guid> Bad(Guid id, string error) => BaseCommandResponse.Failure<Guid>(
        "event_ticketing_validation_failed", "Ticketing configuration is invalid.", [error], id);

    private static BaseCommandResponse<Guid> Conflict(Guid id, string error) => BaseCommandResponse.Failure<Guid>(
        "event_ticketing_concurrency_conflict", "Ticketing configuration was updated by another request.", [error], id);
}
