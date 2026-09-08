using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventAddOns;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.EventAddOns.Requests.Commands;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using MediatR;
using DomainEvent = Explore.Domain.Event;

namespace Explore.Application.Features.EventAddOns.Handlers.Commands;

public sealed class CreateEventAddOnCatalogDraftCommandHandler(
    IEventRepository events,
    IEventAddOnRepository addOns,
    ITenantContext tenant,
    ICurrentUserService currentUser) :
    IRequestHandler<CreateEventAddOnCatalogDraftCommand, EventAddOnCatalogDto?>
{
    public async Task<EventAddOnCatalogDto?> Handle(
        CreateEventAddOnCatalogDraftCommand request,
        CancellationToken cancellationToken)
    {
        DomainEvent? eventTarget =
            await events.GetAuthorizationTargetByIdAsync(request.EventId, cancellationToken);
        if (!EventAddOnAccess.CanManagePlatformEvent(eventTarget, tenant.TenantId, currentUser))
        {
            return null;
        }

        EventAddOnCatalogVersion? latest = await addOns.GetManagementCatalogAsync(
            tenant.TenantId,
            request.EventId,
            cancellationToken);
        if (latest is { PublishedAt: null, RetiredAt: null })
        {
            return await MapCatalogAsync(latest, cancellationToken);
        }

        EventAddOnCatalogVersion catalog = EventAddOnCatalogVersion.Create(
            tenant.TenantId,
            request.EventId,
            request.CurrencyCode,
            checked((latest?.VersionNumber ?? 0) + 1));
        await addOns.AddCatalogAsync(catalog, cancellationToken);
        return await MapCatalogAsync(catalog, cancellationToken);
    }

    private async Task<EventAddOnCatalogDto> MapCatalogAsync(
        EventAddOnCatalogVersion catalog,
        CancellationToken cancellationToken) =>
        EventAddOnMapper.Catalog(
            catalog,
            await addOns.GetAvailableCatalogItemQuantitiesAsync(
                tenant.TenantId,
                catalog.EventId,
                catalog.Id,
                cancellationToken),
            canManage: true);
}

public sealed class AddEventAddOnCatalogItemCommandHandler(
    IEventRepository events,
    IEventAddOnRepository addOns,
    ITenantContext tenant,
    ICurrentUserService currentUser) :
    IRequestHandler<AddEventAddOnCatalogItemCommand, EventAddOnCatalogDto?>
{
    public async Task<EventAddOnCatalogDto?> Handle(
        AddEventAddOnCatalogItemCommand request,
        CancellationToken cancellationToken)
    {
        DomainEvent? eventTarget =
            await events.GetAuthorizationTargetByIdAsync(request.EventId, cancellationToken);
        EventAddOnCatalogVersion? catalog =
            await addOns.GetDraftCatalogForUpdateAsync(
                tenant.TenantId,
                request.EventId,
                cancellationToken);
        if (!EventAddOnAccess.CanManagePlatformEvent(eventTarget, tenant.TenantId, currentUser) ||
            catalog is null)
        {
            return null;
        }

        EventAddOnCatalogItem item = EventAddOnCatalogItem.Create(
            Guid.CreateVersion7(),
            tenant.TenantId,
            catalog.Id,
            request.Name,
            request.Description,
            Money.Create(request.UnitPriceMinor, catalog.CurrencyCode),
            request.InventoryCapacity,
            request.FulfillmentDisclosure,
            request.RefundDisclosure);
        catalog.AddItem(item);
        await addOns.SaveChangesAsync(cancellationToken);
        return EventAddOnMapper.Catalog(
            catalog,
            catalog.Items.ToDictionary(
                value => value.Id,
                value => value.InventoryCapacity),
            canManage: true);
    }
}

public sealed class PublishEventAddOnCatalogCommandHandler(
    IEventRepository events,
    IEventAddOnRepository addOns,
    IUnitOfWork unitOfWork,
    ITenantContext tenant,
    ICurrentUserService currentUser) :
    IRequestHandler<PublishEventAddOnCatalogCommand, EventAddOnCatalogDto?>
{
    public async Task<EventAddOnCatalogDto?> Handle(
        PublishEventAddOnCatalogCommand request,
        CancellationToken cancellationToken)
    {
        DomainEvent? eventTarget =
            await events.GetAuthorizationTargetByIdAsync(request.EventId, cancellationToken);
        if (!EventAddOnAccess.CanManagePlatformEvent(eventTarget, tenant.TenantId, currentUser))
        {
            return null;
        }

        EventAddOnCatalogVersion? published = await unitOfWork.ExecuteSerializableAsync(
            async token =>
            {
                EventAddOnCatalogVersion? draft =
                    await addOns.GetDraftCatalogForUpdateAsync(
                        tenant.TenantId,
                        request.EventId,
                        token);
                if (draft is null)
                {
                    return null;
                }

                EventAddOnCatalogVersion? current =
                    await addOns.GetPublishedCatalogForUpdateAsync(
                        tenant.TenantId,
                        request.EventId,
                        token);
                current?.Retire(request.PublishedAtUtc);
                draft.Publish(request.PublishedAtUtc);
                await addOns.SaveChangesAsync(token);
                return draft;
            },
            cancellationToken);
        return published is null
            ? null
            : EventAddOnMapper.Catalog(
                published,
                published.Items.ToDictionary(
                    item => item.Id,
                    item => item.InventoryCapacity),
                canManage: true);
    }
}

public sealed class RetireEventAddOnCatalogCommandHandler(
    IEventRepository events,
    IEventAddOnRepository addOns,
    ITenantContext tenant,
    ICurrentUserService currentUser) :
    IRequestHandler<RetireEventAddOnCatalogCommand, EventAddOnCatalogDto?>
{
    public async Task<EventAddOnCatalogDto?> Handle(
        RetireEventAddOnCatalogCommand request,
        CancellationToken cancellationToken)
    {
        DomainEvent? eventTarget =
            await events.GetAuthorizationTargetByIdAsync(request.EventId, cancellationToken);
        EventAddOnCatalogVersion? catalog =
            await addOns.GetPublishedCatalogForUpdateAsync(
                tenant.TenantId,
                request.EventId,
                cancellationToken);
        if (!EventAddOnAccess.CanManagePlatformEvent(eventTarget, tenant.TenantId, currentUser) ||
            catalog is null)
        {
            return null;
        }

        catalog.Retire(request.RetiredAtUtc);
        await addOns.SaveChangesAsync(cancellationToken);
        return EventAddOnMapper.Catalog(
            catalog,
            new Dictionary<Guid, int>(),
            canManage: true);
    }
}

public sealed class ReserveRegistrationOrderAddOnsCommandHandler(
    IEventAddOnRepository addOns,
    IUnitOfWork unitOfWork,
    ITenantContext tenant,
    ICurrentUserService currentUser) :
    IRequestHandler<ReserveRegistrationOrderAddOnsCommand, RegistrationOrderAddOnSummaryDto?>
{
    public async Task<RegistrationOrderAddOnSummaryDto?> Handle(
        ReserveRegistrationOrderAddOnsCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated ||
            !currentUser.UserId.HasValue ||
            request.CatalogId == Guid.Empty ||
            !EventAddOnAccess.ValidSelections(request.Selections))
        {
            return null;
        }

        Guid[] lineIds = request.Selections.Select(_ => Guid.CreateVersion7()).ToArray();
        Guid[] allocationIds = request.Selections.Select(_ => Guid.CreateVersion7()).ToArray();
        try
        {
            RegistrationOrder? updated = await unitOfWork.ExecuteSerializableAsync(
                async token =>
                {
                    RegistrationOrder? order =
                        await addOns.GetOrderForAddOnUpdateAsync(
                            tenant.TenantId,
                            request.EventId,
                            request.RegistrationOrderId,
                            token);
                    if (order is null ||
                        order.AccountUserId != currentUser.UserId ||
                        order.RegistrationOrderStatusId != (int)RegistrationOrderStatusEnum.Draft ||
                        order.Lines.Count == 0 ||
                        order.AddOnLines.Count != 0 ||
                        order.ExpiresAt <= request.ReservedAtUtc)
                    {
                        return null;
                    }

                    EventAddOnCatalogVersion? catalog =
                        await addOns.GetPublishedCatalogByIdAsync(
                            tenant.TenantId,
                            request.EventId,
                            request.CatalogId,
                            token);
                    if (catalog is null ||
                        !string.Equals(
                            catalog.CurrencyCode,
                            order.CurrencyCode,
                            StringComparison.Ordinal))
                    {
                        return null;
                    }

                    order.VerifyCommercialTotals();
                    order.PinAddOnCatalog(catalog);
                    Dictionary<Guid, EventAddOnCatalogItem> items =
                        catalog.Items.ToDictionary(item => item.Id);
                    for (int index = 0; index < request.Selections.Count; index++)
                    {
                        EventAddOnSelection selection = request.Selections[index];
                        if (!items.TryGetValue(selection.CatalogItemId, out EventAddOnCatalogItem? item))
                        {
                            return null;
                        }

                        order.AddAddOnLine(RegistrationOrderAddOnLine.Create(
                            lineIds[index],
                            order,
                            catalog,
                            item,
                            selection.Quantity));
                    }

                    long organizerTotal = MinorUnitMath.Add(
                        order.Lines.Aggregate(
                            0L,
                            static (total, line) => MinorUnitMath.Add(
                                total,
                                line.PostDiscountLineSubtotalMinorSnapshot)),
                        order.AddOnTotalMinorSnapshot);
                    order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create(
                        order.CurrencyCode,
                        organizerTotal,
                        order.PlatformFeeTotalMinorSnapshot,
                        checked(organizerTotal - order.PlatformFeeTotalMinorSnapshot),
                        order.PlatformContribution?.AmountMinor ?? 0));
                    await addOns.SaveChangesAsync(token);
                    for (int index = 0; index < order.AddOnLines.Count; index++)
                    {
                        RegistrationOrderAddOnLine line = order.AddOnLines.ElementAt(index);
                        EventAddOnInventoryResult reservation =
                            await addOns.ReserveInventoryAsync(
                                tenant.TenantId,
                                request.EventId,
                                line.Id,
                                allocationIds[index],
                                request.ReservedAtUtc,
                                token);
                        if (reservation.Outcome != EventAddOnInventoryOutcome.Reserved)
                        {
                            throw new EventAddOnSelectionUnavailableException();
                        }
                    }

                    return order;
                },
                cancellationToken);
            return updated is null
                ? null
                : await EventAddOnMapper.OrderAsync(
                    addOns,
                    updated,
                    canReserve: false,
                    canManage: false,
                    cancellationToken);
        }
        catch (EventAddOnSelectionUnavailableException)
        {
            return null;
        }
    }
}

public sealed class FulfillRegistrationOrderAddOnCommandHandler(
    IEventRepository events,
    IEventAddOnRepository addOns,
    ITenantContext tenant,
    ICurrentUserService currentUser) :
    IRequestHandler<FulfillRegistrationOrderAddOnCommand, RegistrationOrderAddOnSummaryDto?>
{
    public async Task<RegistrationOrderAddOnSummaryDto?> Handle(
        FulfillRegistrationOrderAddOnCommand request,
        CancellationToken cancellationToken)
    {
        DomainEvent? eventTarget =
            await events.GetAuthorizationTargetByIdAsync(request.EventId, cancellationToken);
        RegistrationOrder? targetOrder = await addOns.GetOrderWithAddOnsAsync(
            tenant.TenantId,
            request.EventId,
            request.RegistrationOrderId,
            cancellationToken);
        if (!EventAddOnAccess.CanManagePlatformEvent(eventTarget, tenant.TenantId, currentUser))
        {
            return null;
        }
        if (targetOrder is null ||
            targetOrder.RegistrationOrderStatusId !=
            (int)RegistrationOrderStatusEnum.Confirmed ||
            targetOrder.AddOnLines.All(line =>
                line.Id != request.RegistrationOrderAddOnLineId))
        {
            return null;
        }

        EventAddOnFulfillmentResult result = await addOns.FulfillAsync(
            tenant.TenantId,
            request.EventId,
            request.RegistrationOrderAddOnLineId,
            request.OperationId,
            request.FulfilledAtUtc,
            cancellationToken);
        return result.Outcome is EventAddOnFulfillmentOutcome.Fulfilled or
            EventAddOnFulfillmentOutcome.AlreadyFulfilled
            ? await SummaryAsync(request, cancellationToken)
            : null;
    }

    private async Task<RegistrationOrderAddOnSummaryDto?> SummaryAsync(
        FulfillRegistrationOrderAddOnCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationOrder? order = await addOns.GetOrderWithAddOnsAsync(
            tenant.TenantId,
            request.EventId,
            request.RegistrationOrderId,
            cancellationToken);
        return order is null
            ? null
            : await EventAddOnMapper.OrderAsync(
                addOns,
                order,
                canReserve: false,
                canManage: true,
                cancellationToken);
    }
}

public sealed class RefundRegistrationOrderAddOnCommandHandler(
    IEventRepository events,
    IEventAddOnRepository addOns,
    RegistrationRefundService refunds,
    IUnitOfWork unitOfWork,
    ITenantContext tenant,
    ICurrentUserService currentUser) :
    IRequestHandler<RefundRegistrationOrderAddOnCommand, RegistrationOrderAddOnSummaryDto?>
{
    public async Task<RegistrationOrderAddOnSummaryDto?> Handle(
        RefundRegistrationOrderAddOnCommand request,
        CancellationToken cancellationToken)
    {
        DomainEvent? eventTarget =
            await events.GetAuthorizationTargetByIdAsync(request.EventId, cancellationToken);
        RegistrationOrder? targetOrder = await addOns.GetOrderWithAddOnsAsync(
            tenant.TenantId,
            request.EventId,
            request.RegistrationOrderId,
            cancellationToken);
        if (!EventAddOnAccess.CanManagePlatformEvent(eventTarget, tenant.TenantId, currentUser))
        {
            return null;
        }
        if (targetOrder is null ||
            targetOrder.RegistrationOrderStatusId !=
            (int)RegistrationOrderStatusEnum.Confirmed ||
            targetOrder.AddOnLines.All(line =>
                line.Id != request.RegistrationOrderAddOnLineId))
        {
            return null;
        }

        RegistrationOrderAddOnLine targetLine = targetOrder.AddOnLines.Single(
            line => line.Id == request.RegistrationOrderAddOnLineId);
        if (request.Quantity <= 0 || request.Quantity > targetLine.Quantity)
        {
            return null;
        }

        EventAddOnRefundResult result;
        try
        {
            result = await unitOfWork.ExecuteSerializableAsync(
                async token =>
                {
                    long amountMinor = MinorUnitMath.Multiply(
                        targetLine.UnitPriceMinorSnapshot,
                        request.Quantity);
                    RegistrationRefundCommandResultDto providerReservation =
                        await refunds.InitiateAsync(
                            targetOrder,
                            amountMinor,
                            request.OperationId.ToString("N"),
                            eventTarget!.OrganizerActorId ??
                            eventTarget.ActorId,
                            "organizer",
                            "event_add_on",
                            token);
                    if (!providerReservation.IsSuccess ||
                        providerReservation.Id == Guid.Empty)
                    {
                        throw new EventAddOnRefundReservationException();
                    }

                    EventAddOnRefundResult allocation =
                        await addOns.AllocateRefundAsync(
                            tenant.TenantId,
                            request.EventId,
                            request.RegistrationOrderAddOnLineId,
                            providerReservation.Id,
                            request.Quantity,
                            request.AllocatedAtUtc,
                            token);
                    if (allocation.Outcome is not (
                        EventAddOnRefundOutcome.Allocated or
                        EventAddOnRefundOutcome.AlreadyAllocated))
                    {
                        throw new EventAddOnRefundReservationException();
                    }

                    return allocation;
                },
                cancellationToken);
        }
        catch (EventAddOnRefundReservationException)
        {
            return null;
        }

        if (result.Outcome is not (
            EventAddOnRefundOutcome.Allocated or
            EventAddOnRefundOutcome.AlreadyAllocated))
        {
            return null;
        }

        RegistrationOrder? order = await addOns.GetOrderWithAddOnsAsync(
            tenant.TenantId,
            request.EventId,
            request.RegistrationOrderId,
            cancellationToken);
        return order is null
            ? null
            : await EventAddOnMapper.OrderAsync(
                addOns,
                order,
                canReserve: false,
                canManage: true,
                cancellationToken);
    }
}

