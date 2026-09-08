using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventAddOns;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.EventAddOns.Requests.Queries;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using MediatR;
using DomainEvent = Explore.Domain.Event;

namespace Explore.Application.Features.EventAddOns.Handlers.Queries;

public sealed class GetEventAddOnCatalogQueryHandler(
    IEventRepository events,
    IEventAddOnRepository addOns,
    ITenantContext tenant,
    ICurrentUserService currentUser) :
    IRequestHandler<GetEventAddOnCatalogQuery, EventAddOnCatalogDto?>
{
    public async Task<EventAddOnCatalogDto?> Handle(
        GetEventAddOnCatalogQuery request,
        CancellationToken cancellationToken)
    {
        DomainEvent? eventTarget =
            await events.GetAuthorizationTargetByIdAsync(request.EventId, cancellationToken);
        if (!EventAddOnAccess.IsPlatformManaged(eventTarget, tenant.TenantId))
        {
            return null;
        }

        bool canManage = EventAddOnAccess.CanManage(eventTarget!, currentUser);
        if (request.ManagementView && !canManage)
        {
            return null;
        }

        EventAddOnCatalogVersion? catalog = request.ManagementView
            ? await addOns.GetManagementCatalogAsync(
                tenant.TenantId,
                request.EventId,
                cancellationToken)
            : await addOns.GetPublishedCatalogAsync(
                tenant.TenantId,
                request.EventId,
                cancellationToken);
        if (catalog is null)
        {
            return request.ManagementView
                ? new EventAddOnCatalogDto
                {
                    EventId = request.EventId,
                    Items = [],
                    CanManage = true,
                    CanCreateDraft = true,
                    IsManagementView = true,
                }
                : null;
        }

        IReadOnlyDictionary<Guid, int> available =
            await addOns.GetAvailableCatalogItemQuantitiesAsync(
                tenant.TenantId,
                request.EventId,
                catalog.Id,
                cancellationToken);
        return EventAddOnMapper.Catalog(
            catalog,
            available,
            canManage,
            request.ManagementView);
    }
}

public sealed class GetRegistrationOrderAddOnsQueryHandler(
    IEventRepository events,
    IEventAddOnRepository addOns,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    IGuestCapabilityTokenService guestTokens,
    TimeProvider timeProvider) :
    IRequestHandler<GetRegistrationOrderAddOnsQuery, RegistrationOrderAddOnSummaryDto?>
{
    public async Task<RegistrationOrderAddOnSummaryDto?> Handle(
        GetRegistrationOrderAddOnsQuery request,
        CancellationToken cancellationToken)
    {
        RegistrationOrder? order = await addOns.GetOrderWithAddOnsAsync(
            tenant.TenantId,
            request.EventId,
            request.RegistrationOrderId,
            cancellationToken);
        if (order is null ||
            !EventAddOnAccess.CanAccessOrder(order, currentUser, request.Capability, guestTokens))
        {
            return null;
        }

        DomainEvent? eventTarget =
            await events.GetAuthorizationTargetByIdAsync(request.EventId, cancellationToken);
        bool canManage = EventAddOnAccess.CanManage(eventTarget, currentUser);
        EventAddOnCatalogVersion? catalog =
            await addOns.GetPublishedCatalogAsync(
                tenant.TenantId,
                request.EventId,
                cancellationToken);
        return await EventAddOnMapper.OrderAsync(
            addOns,
            order,
            canReserve:
                currentUser.IsAuthenticated &&
                currentUser.UserId.HasValue &&
                order.AccountUserId == currentUser.UserId &&
                order.RegistrationOrderStatusId == (int)RegistrationOrderStatusEnum.Draft &&
                order.ExpiresAt > timeProvider.GetUtcNow().UtcDateTime &&
                order.Lines.Count > 0 &&
                order.AddOnLines.Count == 0 &&
                catalog is { Items.Count: > 0 },
            canManage,
            cancellationToken);
    }
}

