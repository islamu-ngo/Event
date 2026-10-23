using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventAddOns;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.EventAddOns.Requests.Queries;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using DomainEvent = Explore.Domain.Event;

namespace Explore.Application.Features.EventAddOns.Handlers.Queries;

public sealed class GetEventAddOnCatalogQueryHandler(
    IEventRepository events,
    IEventAddOnRepository addOns,
    ITenantContext tenant,
    ICurrentUserService currentUser) :
    IQueryHandler<GetEventAddOnCatalogQuery, EventAddOnCatalogDto?>
{
    public async Task<EventAddOnCatalogDto?> QueryAsync(
        GetEventAddOnCatalogQuery request,
        CancellationToken cancellationToken)
    {
        DomainEvent? eventTarget =
            await events.GetAuthorizationTargetByIdAsync(request.EventId, cancellationToken);
        if (!EventAddOnAccess.IsPlatformManaged(eventTarget, tenant.TenantId))
        {
            return null;
        }

        if (!request.ManagementView &&
            !await events.IsPubliclyEligibleAsync(tenant.TenantId, request.EventId, cancellationToken))
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
    IRegistrationPrivateReader reader,
    TimeProvider timeProvider) :
    IQueryHandler<GetRegistrationOrderAddOnsQuery, RegistrationOrderAddOnSummaryDto?>
{
    public async Task<RegistrationOrderAddOnSummaryDto?> QueryAsync(
        GetRegistrationOrderAddOnsQuery request,
        CancellationToken cancellationToken)
    {
        var graph = await reader.ReadAsync(new(RegistrationPrivateReadKind.AddOns,
            request.EventId, request.RegistrationOrderId), request.Capability, cancellationToken);
        if (graph is null)
        {
            return null;
        }

        RegistrationOrder order = graph.Order;
        bool canManage = graph.PrincipalId is { } user
            && (graph.Event?.Actor?.UserId == user || graph.Event?.OrganizerActor?.UserId == user);
        return EventAddOnMapper.Order(
            order,
            graph.Fulfillments,
            graph.Refunds,
            canReserve:
                graph.PrincipalId.HasValue &&
                order.AccountUserId == graph.PrincipalId &&
                order.RegistrationOrderStatusId == (int)RegistrationOrderStatusEnum.Draft &&
                order.ExpiresAt > timeProvider.GetUtcNow().UtcDateTime &&
                order.Lines.Count > 0 &&
                order.AddOnLines.Count == 0 &&
                graph.AddOnCatalog is { Items.Count: > 0 },
            canManage);
    }
}

