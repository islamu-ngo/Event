using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed class EventAddOnRepository(ExploreDbContext dbContext) : IEventAddOnRepository
{
    public const string CanonicalFenceOrder =
        "catalog-item>order>line>inventory>fulfillment>refund";

    public Task<EventAddOnCatalogVersion?> GetPublishedCatalogAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken) =>
        dbContext.EventAddOnCatalogVersions
            .AsNoTracking()
            .Include(catalog => catalog.Items)
            .FirstOrDefaultAsync(
                catalog =>
                    catalog.TenantId == tenantId &&
                    catalog.EventId == eventId &&
                    catalog.PublishedAt != null &&
                    catalog.PublishedAt <= DateTime.UtcNow &&
                    catalog.RetiredAt == null,
                cancellationToken);

    public Task<EventAddOnCatalogVersion?> GetPublishedCatalogByIdAsync(
        Guid tenantId,
        Guid eventId,
        Guid catalogId,
        CancellationToken cancellationToken) =>
        dbContext.EventAddOnCatalogVersions
            .AsNoTracking()
            .Include(catalog => catalog.Items)
            .FirstOrDefaultAsync(
                catalog =>
                    catalog.TenantId == tenantId &&
                    catalog.EventId == eventId &&
                    catalog.Id == catalogId &&
                    catalog.PublishedAt != null &&
                    catalog.PublishedAt <= DateTime.UtcNow &&
                    catalog.RetiredAt == null,
                cancellationToken);

    public Task<EventAddOnCatalogVersion?> GetManagementCatalogAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken) =>
        dbContext.EventAddOnCatalogVersions
            .AsNoTracking()
            .Include(catalog => catalog.Items)
            .Where(catalog =>
                catalog.TenantId == tenantId &&
                catalog.EventId == eventId)
            .OrderByDescending(catalog => catalog.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<EventAddOnCatalogVersion?> GetDraftCatalogForUpdateAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken) =>
        dbContext.EventAddOnCatalogVersions
            .Include(catalog => catalog.Items)
            .Where(catalog =>
                catalog.TenantId == tenantId &&
                catalog.EventId == eventId &&
                catalog.PublishedAt == null &&
                catalog.RetiredAt == null)
            .OrderByDescending(catalog => catalog.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<EventAddOnCatalogVersion?> GetPublishedCatalogForUpdateAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken) =>
        dbContext.EventAddOnCatalogVersions
            .Include(catalog => catalog.Items)
            .FirstOrDefaultAsync(
                catalog =>
                    catalog.TenantId == tenantId &&
                    catalog.EventId == eventId &&
                    catalog.PublishedAt != null &&
                    catalog.PublishedAt <= DateTime.UtcNow &&
                    catalog.RetiredAt == null,
                cancellationToken);

    public async Task AddCatalogAsync(
        EventAddOnCatalogVersion catalog,
        CancellationToken cancellationToken)
    {
        dbContext.EventAddOnCatalogVersions.Add(catalog);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> GetAvailableCatalogItemQuantitiesAsync(
        Guid tenantId,
        Guid eventId,
        Guid catalogId,
        CancellationToken cancellationToken)
    {
        EventAddOnCatalogItem[] items = await dbContext.EventAddOnCatalogItems
            .AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId &&
                item.EventAddOnCatalogVersionId == catalogId)
            .ToArrayAsync(cancellationToken);
        Dictionary<Guid, int> allocatedByItem = await dbContext.EventAddOnInventoryAllocations
            .AsNoTracking()
            .Where(allocation =>
                allocation.TenantId == tenantId &&
                allocation.EventId == eventId &&
                allocation.ActiveUniquenessSlot != null)
            .GroupBy(allocation => allocation.EventAddOnCatalogItemId)
            .Select(group => new
            {
                ItemId = group.Key,
                Quantity = group.Sum(allocation =>
                    allocation.Quantity - allocation.ReleasedQuantity),
            })
            .ToDictionaryAsync(
                value => value.ItemId,
                value => value.Quantity,
                cancellationToken);
        return items.ToDictionary(
            item => item.Id,
            item => Math.Max(
                0,
                item.InventoryCapacity -
                allocatedByItem.GetValueOrDefault(item.Id)));
    }

    public async Task<IReadOnlyList<EventAddOnFulfillment>> ListFulfillmentsAsync(
        Guid tenantId,
        Guid eventId,
        Guid registrationOrderId,
        CancellationToken cancellationToken) =>
        await dbContext.EventAddOnFulfillments
            .AsNoTracking()
            .Where(fulfillment =>
                fulfillment.TenantId == tenantId &&
                fulfillment.EventId == eventId &&
                fulfillment.RegistrationOrderId == registrationOrderId)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<EventAddOnRefundAllocation>> ListRefundAllocationsAsync(
        Guid tenantId,
        Guid eventId,
        Guid registrationOrderId,
        CancellationToken cancellationToken) =>
        await dbContext.EventAddOnRefundAllocations
            .AsNoTracking()
            .Where(allocation =>
                allocation.TenantId == tenantId &&
                allocation.EventId == eventId &&
                allocation.RegistrationOrderId == registrationOrderId)
            .ToArrayAsync(cancellationToken);

    public Task<RegistrationOrder?> GetOrderForAddOnUpdateAsync(
        Guid tenantId,
        Guid eventId,
        Guid registrationOrderId,
        CancellationToken cancellationToken) =>
        dbContext.RegistrationOrders
            .Include(order => order.Lines)
            .Include(order => order.AddOnLines)
            .Include(order => order.PlatformContribution)
            .FirstOrDefaultAsync(
                order =>
                    order.TenantId == tenantId &&
                    order.EventId == eventId &&
                    order.Id == registrationOrderId,
                cancellationToken);

    public Task<EventAddOnInventoryResult> ReserveInventoryAsync(
        Guid tenantId,
        Guid eventId,
        Guid registrationOrderAddOnLineId,
        Guid operationId,
        DateTime reservedAtUtc,
        CancellationToken cancellationToken)
    {
        RequireIdentity(tenantId, eventId, registrationOrderAddOnLineId, operationId);
        RequireUtc(reservedAtUtc, nameof(reservedAtUtc));
        return ExecuteAtomicAsync(
            token => ReserveInventoryCoreAsync(
                tenantId,
                eventId,
                registrationOrderAddOnLineId,
                operationId,
                reservedAtUtc,
                token),
            cancellationToken);
    }

    public Task<EventAddOnFulfillmentResult> FulfillAsync(
        Guid tenantId,
        Guid eventId,
        Guid registrationOrderAddOnLineId,
        Guid operationId,
        DateTime fulfilledAtUtc,
        CancellationToken cancellationToken)
    {
        RequireIdentity(tenantId, eventId, registrationOrderAddOnLineId, operationId);
        RequireUtc(fulfilledAtUtc, nameof(fulfilledAtUtc));
        return ExecuteAtomicAsync(
            token => FulfillCoreAsync(
                tenantId,
                eventId,
                registrationOrderAddOnLineId,
                operationId,
                fulfilledAtUtc,
                token),
            cancellationToken);
    }

    public Task<EventAddOnRefundResult> AllocateRefundAsync(
        Guid tenantId,
        Guid eventId,
        Guid registrationOrderAddOnLineId,
        Guid refundOperationId,
        int quantity,
        DateTime allocatedAtUtc,
        CancellationToken cancellationToken)
    {
        RequireIdentity(
            tenantId,
            eventId,
            registrationOrderAddOnLineId,
            refundOperationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        RequireUtc(allocatedAtUtc, nameof(allocatedAtUtc));
        return ExecuteAtomicAsync(
            token => AllocateRefundCoreAsync(
                tenantId,
                eventId,
                registrationOrderAddOnLineId,
                refundOperationId,
                quantity,
                allocatedAtUtc,
                token),
            cancellationToken);
    }

    public Task<EventAddOnRefundAllocation?> ResolveRefundAsync(
        Guid tenantId,
        Guid refundOperationId,
        bool providerSucceeded,
        DateTime resolvedAtUtc,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || refundOperationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Tenant, event, and refund operation identities are required.");
        }

        RequireUtc(resolvedAtUtc, nameof(resolvedAtUtc));
        return ExecuteAtomicAsync(
            token => ResolveRefundCoreAsync(
                tenantId,
                refundOperationId,
                providerSucceeded,
                resolvedAtUtc,
                token),
            cancellationToken);
    }

    public Task<RegistrationOrder?> GetOrderWithAddOnsAsync(
        Guid tenantId,
        Guid eventId,
        Guid registrationOrderId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || eventId == Guid.Empty || registrationOrderId == Guid.Empty ||
            IsTenantMismatch(tenantId))
        {
            return Task.FromResult<RegistrationOrder?>(null);
        }

        return dbContext.RegistrationOrders
            .AsNoTracking()
            .Include(order => order.AddOnLines)
            .FirstOrDefaultAsync(
                order =>
                    order.TenantId == tenantId &&
                    order.EventId == eventId &&
                    order.Id == registrationOrderId,
                cancellationToken);
    }

    private async Task<EventAddOnInventoryResult> ReserveInventoryCoreAsync(
        Guid tenantId,
        Guid eventId,
        Guid lineId,
        Guid operationId,
        DateTime reservedAtUtc,
        CancellationToken cancellationToken)
    {
        if (IsTenantMismatch(tenantId))
        {
            return EventAddOnInventoryResult.Failure(EventAddOnInventoryOutcome.TenantMismatch);
        }

        EventAddOnInventoryAllocation? replay =
            await dbContext.EventAddOnInventoryAllocations.FirstOrDefaultAsync(
                allocation =>
                    allocation.TenantId == tenantId &&
                    allocation.OperationId == operationId,
                cancellationToken);
        if (replay is not null)
        {
            return replay.RegistrationOrderAddOnLineId == lineId &&
                   replay.EventId == eventId
                ? EventAddOnInventoryResult.Existing(replay)
                : EventAddOnInventoryResult.Failure(EventAddOnInventoryOutcome.TenantMismatch);
        }

        RegistrationOrderAddOnLine? identity = await FindLineAsync(
            tenantId,
            eventId,
            lineId,
            tracking: false,
            cancellationToken);
        if (identity is null)
        {
            return EventAddOnInventoryResult.Failure(EventAddOnInventoryOutcome.NotFound);
        }

        await AcquireCanonicalFencesAsync(identity, cancellationToken);
        replay = await dbContext.EventAddOnInventoryAllocations.FirstOrDefaultAsync(
            allocation =>
                allocation.TenantId == tenantId &&
                allocation.OperationId == operationId,
            cancellationToken);
        if (replay is not null)
        {
            return replay.RegistrationOrderAddOnLineId == lineId &&
                   replay.EventId == eventId
                ? EventAddOnInventoryResult.Existing(replay)
                : EventAddOnInventoryResult.Failure(EventAddOnInventoryOutcome.TenantMismatch);
        }

        EventAddOnInventoryAllocation? existing =
            await dbContext.EventAddOnInventoryAllocations.FirstOrDefaultAsync(
                allocation =>
                    allocation.TenantId == tenantId &&
                    allocation.RegistrationOrderAddOnLineId == lineId,
                cancellationToken);
        if (existing is not null)
        {
            return EventAddOnInventoryResult.Existing(existing);
        }

        RegistrationOrderAddOnLine line = await FindLineAsync(
                tenantId,
                eventId,
                lineId,
                tracking: true,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Add-on order line disappeared after its canonical fence was acquired.");
        EventAddOnCatalogItem item =
            await dbContext.EventAddOnCatalogItems.SingleAsync(
                value =>
                    value.TenantId == tenantId &&
                    value.Id == line.EventAddOnCatalogItemId,
                cancellationToken);
        int allocated = await dbContext.EventAddOnInventoryAllocations
            .Where(allocation =>
                allocation.TenantId == tenantId &&
                allocation.EventAddOnCatalogItemId == item.Id &&
                allocation.ActiveUniquenessSlot != null)
            .SumAsync(
                allocation => (int?)(allocation.Quantity - allocation.ReleasedQuantity),
                cancellationToken) ?? 0;
        if (checked((long)allocated + line.Quantity) > item.InventoryCapacity)
        {
            return EventAddOnInventoryResult.Failure(
                EventAddOnInventoryOutcome.InsufficientInventory);
        }

        EventAddOnInventoryAllocation created = EventAddOnInventoryAllocation.Create(
            Guid.CreateVersion7(),
            operationId,
            line,
            reservedAtUtc);
        dbContext.EventAddOnInventoryAllocations.Add(created);
        await dbContext.SaveChangesAsync(cancellationToken);
        return EventAddOnInventoryResult.Reserved(created);
    }

    private async Task<EventAddOnFulfillmentResult> FulfillCoreAsync(
        Guid tenantId,
        Guid eventId,
        Guid lineId,
        Guid operationId,
        DateTime fulfilledAtUtc,
        CancellationToken cancellationToken)
    {
        if (IsTenantMismatch(tenantId))
        {
            return EventAddOnFulfillmentResult.Failure(EventAddOnFulfillmentOutcome.TenantMismatch);
        }

        EventAddOnFulfillment? replay = await dbContext.EventAddOnFulfillments
            .FirstOrDefaultAsync(
                fulfillment =>
                    fulfillment.TenantId == tenantId &&
                    fulfillment.OperationId == operationId,
                cancellationToken);
        if (replay is not null)
        {
            return replay.RegistrationOrderAddOnLineId == lineId &&
                   replay.EventId == eventId
                ? EventAddOnFulfillmentResult.Existing(replay)
                : EventAddOnFulfillmentResult.Failure(
                    EventAddOnFulfillmentOutcome.TenantMismatch);
        }

        RegistrationOrderAddOnLine? identity = await FindLineAsync(
            tenantId,
            eventId,
            lineId,
            tracking: false,
            cancellationToken);
        if (identity is null)
        {
            return EventAddOnFulfillmentResult.Failure(EventAddOnFulfillmentOutcome.NotFound);
        }

        await AcquireCanonicalFencesAsync(identity, cancellationToken);
        replay = await dbContext.EventAddOnFulfillments.FirstOrDefaultAsync(
            fulfillment =>
                fulfillment.TenantId == tenantId &&
                fulfillment.RegistrationOrderAddOnLineId == lineId,
            cancellationToken);
        if (replay is not null)
        {
            return EventAddOnFulfillmentResult.Existing(replay);
        }

        bool reserved = await dbContext.EventAddOnInventoryAllocations.AnyAsync(
            allocation =>
                allocation.TenantId == tenantId &&
                allocation.RegistrationOrderAddOnLineId == lineId &&
                allocation.ActiveUniquenessSlot != null &&
                allocation.Quantity > allocation.ReleasedQuantity,
            cancellationToken);
        if (!reserved)
        {
            return EventAddOnFulfillmentResult.Failure(
                EventAddOnFulfillmentOutcome.NotReserved);
        }

        RegistrationOrderAddOnLine line = await FindLineAsync(
                tenantId,
                eventId,
                lineId,
                tracking: true,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Add-on order line disappeared after its canonical fence was acquired.");
        EventAddOnFulfillment created = EventAddOnFulfillment.Create(
            Guid.CreateVersion7(),
            operationId,
            line,
            fulfilledAtUtc);
        dbContext.EventAddOnFulfillments.Add(created);
        await dbContext.SaveChangesAsync(cancellationToken);
        return EventAddOnFulfillmentResult.Fulfilled(created);
    }

    private async Task<EventAddOnRefundResult> AllocateRefundCoreAsync(
        Guid tenantId,
        Guid eventId,
        Guid lineId,
        Guid refundOperationId,
        int quantity,
        DateTime allocatedAtUtc,
        CancellationToken cancellationToken)
    {
        if (IsTenantMismatch(tenantId))
        {
            return EventAddOnRefundResult.Failure(EventAddOnRefundOutcome.TenantMismatch);
        }

        EventAddOnRefundAllocation? replay = await dbContext.EventAddOnRefundAllocations
            .FirstOrDefaultAsync(
                allocation =>
                    allocation.TenantId == tenantId &&
                    allocation.RefundOperationId == refundOperationId,
                cancellationToken);
        if (replay is not null)
        {
            if (replay.Status == EventAddOnRefundAllocationStatus.Failed)
            {
                return EventAddOnRefundResult.Failure(
                    EventAddOnRefundOutcome.ProviderFailed);
            }

            return replay.RegistrationOrderAddOnLineId == lineId &&
                   replay.EventId == eventId
                ? EventAddOnRefundResult.Existing(replay)
                : EventAddOnRefundResult.Failure(EventAddOnRefundOutcome.TenantMismatch);
        }

        RegistrationOrderAddOnLine? identity = await FindLineAsync(
            tenantId,
            eventId,
            lineId,
            tracking: false,
            cancellationToken);
        if (identity is null)
        {
            return EventAddOnRefundResult.Failure(EventAddOnRefundOutcome.NotFound);
        }

        await AcquireCanonicalFencesAsync(identity, cancellationToken);
        replay = await dbContext.EventAddOnRefundAllocations.FirstOrDefaultAsync(
            allocation =>
                allocation.TenantId == tenantId &&
                allocation.RefundOperationId == refundOperationId,
            cancellationToken);
        if (replay is not null)
        {
            if (replay.Status == EventAddOnRefundAllocationStatus.Failed)
            {
                return EventAddOnRefundResult.Failure(
                    EventAddOnRefundOutcome.ProviderFailed);
            }

            return replay.RegistrationOrderAddOnLineId == lineId
                ? EventAddOnRefundResult.Existing(replay)
                : EventAddOnRefundResult.Failure(EventAddOnRefundOutcome.TenantMismatch);
        }

        RegistrationOrderAddOnLine line = await FindLineAsync(
                tenantId,
                eventId,
                lineId,
                tracking: true,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Add-on order line disappeared after its canonical fence was acquired.");
        int alreadyRefunded = await dbContext.EventAddOnRefundAllocations
            .Where(allocation =>
                allocation.TenantId == tenantId &&
                allocation.RegistrationOrderAddOnLineId == lineId &&
                allocation.Status != EventAddOnRefundAllocationStatus.Failed)
            .SumAsync(allocation => (int?)allocation.Quantity, cancellationToken) ?? 0;
        if (checked((long)alreadyRefunded + quantity) > line.Quantity)
        {
            return EventAddOnRefundResult.Failure(
                EventAddOnRefundOutcome.ExceedsCapturedAmount);
        }

        EventAddOnInventoryAllocation? inventory =
            await dbContext.EventAddOnInventoryAllocations.FirstOrDefaultAsync(
                allocation =>
                    allocation.TenantId == tenantId &&
                    allocation.RegistrationOrderAddOnLineId == lineId &&
                    allocation.ActiveUniquenessSlot != null,
                cancellationToken);
        if (inventory is null || quantity > inventory.AllocatedQuantity)
        {
            return EventAddOnRefundResult.Failure(
                EventAddOnRefundOutcome.ExceedsCapturedAmount);
        }

        EventAddOnRefundAllocation created = EventAddOnRefundAllocation.Create(
            Guid.CreateVersion7(),
            refundOperationId,
            line,
            quantity,
            allocatedAtUtc);
        dbContext.EventAddOnRefundAllocations.Add(created);
        await dbContext.SaveChangesAsync(cancellationToken);
        return EventAddOnRefundResult.Allocated(created);
    }

    private async Task<EventAddOnRefundAllocation?> ResolveRefundCoreAsync(
        Guid tenantId,
        Guid refundOperationId,
        bool providerSucceeded,
        DateTime resolvedAtUtc,
        CancellationToken cancellationToken)
    {
        if (IsTenantMismatch(tenantId))
        {
            return null;
        }

        EventAddOnRefundAllocation? identity =
            await dbContext.EventAddOnRefundAllocations
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    allocation =>
                        allocation.TenantId == tenantId &&
                        allocation.RefundOperationId == refundOperationId,
                    cancellationToken);
        if (identity is null)
        {
            return null;
        }

        RegistrationOrderAddOnLine? line = await FindLineAsync(
            tenantId,
            identity.EventId,
            identity.RegistrationOrderAddOnLineId,
            tracking: false,
            cancellationToken);
        if (line is null)
        {
            return null;
        }

        await AcquireCanonicalFencesAsync(line, cancellationToken);
        EventAddOnRefundAllocation allocation =
            await dbContext.EventAddOnRefundAllocations.SingleAsync(
                value =>
                    value.TenantId == tenantId &&
                    value.RefundOperationId == refundOperationId,
                cancellationToken);
        if (allocation.Status == EventAddOnRefundAllocationStatus.Confirmed)
        {
            return providerSucceeded ? allocation : null;
        }

        if (allocation.Status == EventAddOnRefundAllocationStatus.Failed)
        {
            return providerSucceeded ? null : allocation;
        }

        if (allocation.Status ==
            EventAddOnRefundAllocationStatus.ConfirmedInventoryReleasePending)
        {
            if (!providerSucceeded)
            {
                return null;
            }

            EventAddOnInventoryAllocation? recoveryInventory =
                await dbContext.EventAddOnInventoryAllocations.FirstOrDefaultAsync(
                    value =>
                        value.TenantId == tenantId &&
                        value.RegistrationOrderAddOnLineId ==
                        allocation.RegistrationOrderAddOnLineId &&
                        value.ActiveUniquenessSlot != null,
                    cancellationToken);
            if (recoveryInventory is null ||
                allocation.Quantity > recoveryInventory.AllocatedQuantity)
            {
                return allocation;
            }

            recoveryInventory.ReleaseQuantity(
                allocation.Quantity,
                resolvedAtUtc);
            allocation.TryCompleteInventoryRelease(resolvedAtUtc);
            await dbContext.SaveChangesAsync(cancellationToken);
            return allocation;
        }

        if (providerSucceeded)
        {
            EventAddOnInventoryAllocation? inventory =
                await dbContext.EventAddOnInventoryAllocations.FirstOrDefaultAsync(
                    value =>
                        value.TenantId == tenantId &&
                        value.RegistrationOrderAddOnLineId ==
                        allocation.RegistrationOrderAddOnLineId &&
                        value.ActiveUniquenessSlot != null,
                    cancellationToken);
            if (inventory is null ||
                allocation.Quantity > inventory.AllocatedQuantity)
            {
                allocation.TryConfirmInventoryReleasePending(resolvedAtUtc);
            }
            else
            {
                inventory.ReleaseQuantity(allocation.Quantity, resolvedAtUtc);
                allocation.TryConfirm(resolvedAtUtc);
            }
        }
        else
        {
            allocation.TryFail(resolvedAtUtc);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return allocation;
    }

    private Task<RegistrationOrderAddOnLine?> FindLineAsync(
        Guid tenantId,
        Guid eventId,
        Guid lineId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        IQueryable<RegistrationOrderAddOnLine> query = dbContext.RegistrationOrderAddOnLines;
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return query.FirstOrDefaultAsync(
            line =>
                line.TenantId == tenantId &&
                line.EventId == eventId &&
                line.Id == lineId,
            cancellationToken);
    }

    private async Task AcquireCanonicalFencesAsync(
        RegistrationOrderAddOnLine line,
        CancellationToken cancellationToken)
    {
        await RelationalEntityRowFence.AcquireAsync<EventAddOnCatalogItem>(
            dbContext,
            line.TenantId,
            item => item.Id,
            line.EventAddOnCatalogItemId,
            cancellationToken);
        await RelationalEntityRowFence.AcquireAsync<RegistrationOrder>(
            dbContext,
            line.TenantId,
            order => order.Id,
            line.RegistrationOrderId,
            cancellationToken);
        await RelationalEntityRowFence.AcquireAsync<RegistrationOrderAddOnLine>(
            dbContext,
            line.TenantId,
            value => value.Id,
            line.Id,
            cancellationToken);
    }

    private async Task<T> ExecuteAtomicAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            if (dbContext.Database.CurrentTransaction
                    .GetDbTransaction()
                    .IsolationLevel != IsolationLevel.Serializable)
            {
                throw new InvalidOperationException(
                    "Add-on lifecycle writes require a serializable transaction.");
            }

            return await operation(cancellationToken);
        }

        IExecutionStrategy strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async token =>
        {
            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    token);
            T result = await operation(token);
            await transaction.CommitAsync(token);
            return result;
        }, cancellationToken);
    }

    private bool IsTenantMismatch(Guid tenantId) =>
        !dbContext.IsTenantFilterBypassed &&
        dbContext.TenantFilterTenantId != tenantId;

    private static void RequireIdentity(
        Guid tenantId,
        Guid eventId,
        Guid lineId,
        Guid operationId)
    {
        if (tenantId == Guid.Empty ||
            eventId == Guid.Empty ||
            lineId == Guid.Empty ||
            operationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Tenant, event, add-on line, and operation identities are required.");
        }
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be UTC.", parameterName);
        }
    }
}
