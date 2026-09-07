using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventAddOns;
using Explore.Application.Features.EventAddOns.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using DomainEvent = Explore.Domain.Event;

namespace Explore.Application.Features.EventAddOns;

internal static class EventAddOnAccess
{
    internal static bool IsPlatformManaged(DomainEvent? eventTarget, Guid tenantId) =>
        eventTarget?.TenantId == tenantId &&
        eventTarget.ParticipationConfiguration?.ParticipationHandlingModeId ==
        (int)ParticipationHandlingModeEnum.PlatformManaged;

    internal static bool CanManagePlatformEvent(
        DomainEvent? eventTarget,
        Guid tenantId,
        ICurrentUserService currentUser) =>
        IsPlatformManaged(eventTarget, tenantId) &&
        CanManage(eventTarget, currentUser);

    internal static bool CanManage(
        DomainEvent? eventTarget,
        ICurrentUserService currentUser) =>
        eventTarget is not null &&
        currentUser.IsAuthenticated &&
        currentUser.UserId.HasValue &&
        (eventTarget.Actor?.UserId == currentUser.UserId ||
         eventTarget.OrganizerActor?.UserId == currentUser.UserId);

    internal static bool CanAccessOrder(
        RegistrationOrder order,
        ICurrentUserService currentUser,
        string? capability,
        IGuestCapabilityTokenService guestTokens) =>
        currentUser.IsAuthenticated &&
        currentUser.UserId.HasValue &&
        order.AccountUserId == currentUser.UserId ||
        order.AccountUserId is null &&
        order.GuestAccessTokenHash is not null &&
        guestTokens.Matches(capability, order.GuestAccessTokenHash);

    internal static bool ValidSelections(IReadOnlyList<EventAddOnSelection> selections) =>
        selections.Count is > 0 and <= 64 &&
        selections.All(selection =>
            selection.CatalogItemId != Guid.Empty &&
            selection.Quantity > 0) &&
        selections.Select(selection => selection.CatalogItemId).Distinct().Count() ==
        selections.Count;
}

internal static class EventAddOnMapper
{
    internal static EventAddOnCatalogDto Catalog(
        EventAddOnCatalogVersion catalog,
        IReadOnlyDictionary<Guid, int> available,
        bool canManage,
        bool isManagementView = false) =>
        new()
        {
            Id = catalog.Id,
            EventId = catalog.EventId,
            VersionNumber = catalog.VersionNumber,
            CurrencyCode = catalog.CurrencyCode,
            Items = catalog.Items
                .OrderBy(item => item.Name, StringComparer.Ordinal)
                .Select(item => new EventAddOnCatalogItemDto
                {
                    Id = item.Id,
                    Name = item.Name,
                    Description = item.Description,
                    UnitPriceMinor = item.UnitPriceMinor,
                    CurrencyCode = item.CurrencyCode,
                    IsAvailable =
                        catalog.IsPublished &&
                        available.GetValueOrDefault(item.Id) > 0,
                    MaximumSelectableQuantity =
                        available.GetValueOrDefault(item.Id),
                    FulfillmentDisclosure = item.FulfillmentDisclosure,
                    RefundDisclosure = item.RefundDisclosure,
                })
                .ToArray(),
            CanManage = canManage,
            CanCreateDraft = canManage && catalog.IsPublished,
            CanAddItem = canManage && catalog.PublishedAt is null,
            CanPublish = canManage && catalog.PublishedAt is null && catalog.Items.Count > 0,
            CanRetire = canManage && catalog.IsPublished,
            IsManagementView = isManagementView,
        };

    internal static async Task<RegistrationOrderAddOnSummaryDto> OrderAsync(
        IEventAddOnRepository addOns,
        RegistrationOrder order,
        bool canReserve,
        bool canManage,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<EventAddOnFulfillment> fulfillments =
            await addOns.ListFulfillmentsAsync(
                order.TenantId,
                order.EventId,
                order.Id,
                cancellationToken);
        IReadOnlyList<EventAddOnRefundAllocation> refunds =
            await addOns.ListRefundAllocationsAsync(
                order.TenantId,
                order.EventId,
                order.Id,
                cancellationToken);
        HashSet<Guid> fulfilledLines = fulfillments
            .Select(value => value.RegistrationOrderAddOnLineId)
            .ToHashSet();
        Dictionary<Guid, EventAddOnRefundAllocation[]> refundsByLine = refunds
            .GroupBy(value => value.RegistrationOrderAddOnLineId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        bool lifecycleActionsOpen =
            canManage &&
            order.RegistrationOrderStatusId ==
            (int)RegistrationOrderStatusEnum.Confirmed;
        return new RegistrationOrderAddOnSummaryDto
        {
            EventId = order.EventId,
            RegistrationOrderId = order.Id,
            CurrencyCode = order.CurrencyCode,
            AddOnTotalMinor = order.AddOnTotalMinorSnapshot,
            GrandTotalMinor = order.TotalDueMinorSnapshot,
            CanReserve = canReserve,
            Lines = order.AddOnLines
                .OrderBy(line => line.NameSnapshot, StringComparer.Ordinal)
                .Select(line =>
                {
                    EventAddOnRefundAllocation[] lineRefunds =
                        refundsByLine.GetValueOrDefault(line.Id) ?? [];
                    EventAddOnRefundAllocation[] activeRefunds = lineRefunds
                        .Where(value =>
                            value.Status != EventAddOnRefundAllocationStatus.Failed)
                        .ToArray();
                    int refundAllocatedQuantity =
                        activeRefunds.Sum(value => value.Quantity);
                    int confirmedRefundQuantity = activeRefunds
                        .Where(value =>
                            value.Status is
                                EventAddOnRefundAllocationStatus.Confirmed or
                                EventAddOnRefundAllocationStatus
                                    .ConfirmedInventoryReleasePending)
                        .Sum(value => value.Quantity);
                    bool refundPending = activeRefunds.Any(value =>
                        value.Status ==
                        EventAddOnRefundAllocationStatus.PendingProvider);
                    bool inventoryReleasePending = activeRefunds.Any(value =>
                        value.Status ==
                        EventAddOnRefundAllocationStatus
                            .ConfirmedInventoryReleasePending);
                    return new RegistrationOrderAddOnLineDto
                    {
                        Id = line.Id,
                        EventId = line.EventId,
                        RegistrationOrderId = line.RegistrationOrderId,
                        CatalogItemId = line.EventAddOnCatalogItemId,
                        Name = line.NameSnapshot,
                        Quantity = line.Quantity,
                        UnitPriceMinor = line.UnitPriceMinorSnapshot,
                        LineTotalMinor = line.LineTotalMinorSnapshot,
                        CurrencyCode = line.CurrencyCodeSnapshot,
                        FulfillmentDisclosure = line.FulfillmentDisclosureSnapshot,
                        RefundDisclosure = line.RefundDisclosureSnapshot,
                        FulfillmentStatusCode =
                            fulfilledLines.Contains(line.Id) ? "fulfilled" : "pending",
                        RefundAllocatedQuantity = refundAllocatedQuantity,
                        RefundAllocatedMinor = activeRefunds.Aggregate(
                            0L,
                            static (total, value) =>
                                MinorUnitMath.Add(total, value.AmountMinor)),
                        RefundStatusCode = refundPending
                            ? "allocated_pending_provider"
                            : activeRefunds.Any(value =>
                                value.Status ==
                                EventAddOnRefundAllocationStatus
                                    .ConfirmedInventoryReleasePending)
                                ? "provider_confirmed_inventory_release_pending"
                            : confirmedRefundQuantity > 0
                                ? "provider_confirmed"
                                : lineRefunds.Any(value =>
                                    value.Status ==
                                    EventAddOnRefundAllocationStatus.Failed)
                                    ? "provider_failed"
                                    : "not_requested",
                        MaximumRefundableQuantity =
                            refundPending || inventoryReleasePending
                            ? 0
                            : Math.Max(
                                0,
                                line.Quantity - confirmedRefundQuantity),
                        CanFulfill = lifecycleActionsOpen &&
                                     !fulfilledLines.Contains(line.Id) &&
                                     refundAllocatedQuantity == 0,
                        CanRefund = lifecycleActionsOpen &&
                                    line.LineTotalMinorSnapshot > 0 &&
                                    !refundPending &&
                                    !inventoryReleasePending &&
                                    confirmedRefundQuantity < line.Quantity,
                    };
                })
                .ToArray(),
        };
    }
}

internal sealed class EventAddOnSelectionUnavailableException : Exception;

internal sealed class EventAddOnRefundReservationException : Exception;
