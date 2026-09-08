// ABOUTME: EF Core repository for ordered capacity-pool locks and registration inventory hold persistence.
// ABOUTME: Counts active and consumed reservations together so capacity cannot be reallocated after checkout.

using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed class RegistrationInventoryRepository(ExploreDbContext dbContext) : IRegistrationInventoryRepository
{
    public Task<RegistrationOrder?> GetOrderByIdAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        dbContext.RegistrationOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(
                order => order.Id == orderId && order.TenantId == tenantId,
                cancellationToken);

    public Task<RegistrationOrder?> GetOrderWithPiiAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        dbContext.RegistrationOrders
            .AsNoTracking()
            .Include(order => order.Pii)
            .FirstOrDefaultAsync(
                order =>
                    order.Id == orderId
                    && order.TenantId == tenantId,
                cancellationToken);

    public Task<RegistrationOrder?> GetOrderWithLinesAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        dbContext.RegistrationOrders
            .AsNoTracking()
            .Include(order => order.Lines)
            .Include(order => order.PlatformContribution)
            .FirstOrDefaultAsync(
                order => order.Id == orderId && order.TenantId == tenantId,
                cancellationToken);

    public async Task<RegistrationOrder?> GetOrderForUpdateWithLinesAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await AcquireOrderLockIfTransactionalAsync(tenantId, orderId, cancellationToken);
        return await dbContext.RegistrationOrders
            .Include(order => order.Lines)
            .Include(order => order.PlatformContribution)
            .FirstOrDefaultAsync(
                order => order.Id == orderId && order.TenantId == tenantId,
                cancellationToken);
    }

    public async Task<RegistrationOrder?> GetOrderForUpdateWithPiiAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await AcquireOrderLockIfTransactionalAsync(tenantId, orderId, cancellationToken);
        return await dbContext.RegistrationOrders
            .Include(order => order.Lines)
            .Include(order => order.PlatformContribution)
            .Include(order => order.Pii)
            .FirstOrDefaultAsync(
                order => order.Id == orderId && order.TenantId == tenantId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<RegistrationInventoryHold>> GetHoldsByOrderAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        await dbContext.RegistrationInventoryHolds
            .AsNoTracking()
            .Where(hold => hold.RegistrationOrderId == orderId && hold.TenantId == tenantId)
            .OrderBy(hold => hold.Id)
            .ToListAsync(cancellationToken);

    public Task<bool> HasPaidEvidenceAsync(
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        (from payment in dbContext.PaymentAttempts.AsNoTracking()
         join order in dbContext.RegistrationOrders.AsNoTracking()
             on new { payment.TenantId, payment.RegistrationOrderId }
             equals new { order.TenantId, RegistrationOrderId = order.Id }
         where payment.TenantId == tenantId && order.EventId == eventId &&
               (payment.PaymentAttemptStatusId == (int)PaymentAttemptStatusEnum.Succeeded ||
                payment.PaidOrderAcceptanceSnapshotId != null)
         select payment.Id).AnyAsync(cancellationToken);

    public async Task<IReadOnlyList<RegistrationInventoryHold>> GetActiveHoldsForUpdateAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        await dbContext.RegistrationInventoryHolds
            .Where(hold => hold.RegistrationOrderId == orderId &&
                           hold.TenantId == tenantId &&
                           hold.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Active)
            .OrderBy(hold => hold.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RegistrationOrder>> GetOrdersByEventAsync(
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        await dbContext.RegistrationOrders
            .AsNoTracking()
            .Include(order => order.Lines)
            .Include(order => order.PlatformContribution)
            .Where(order => order.EventId == eventId && order.TenantId == tenantId)
            .OrderByDescending(order => order.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetRegisteredUserFanoutBatchAsync(
        Guid tenantId,
        Guid eventId,
        Guid? afterUserId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || eventId == Guid.Empty || batchSize <= 0)
        {
            return [];
        }

        return await dbContext.RegistrationOrders
            .AsNoTracking()
            .Where(order => order.TenantId == tenantId
                && order.EventId == eventId
                && order.AccountUserId != null
                && order.RegistrationOrderStatusId != (int)RegistrationOrderStatusEnum.Expired
                && order.RegistrationOrderStatusId != (int)RegistrationOrderStatusEnum.Cancelled
                && order.RegistrationOrderStatusId != (int)RegistrationOrderStatusEnum.Rejected
                && (!afterUserId.HasValue || order.AccountUserId > afterUserId))
            .Select(order => order.AccountUserId!.Value)
            .Distinct()
            .OrderBy(userId => userId)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationFanoutAudienceMember>> GetNotificationFanoutAudienceBatchAsync(
        Guid tenantId,
        Guid eventId,
        Guid? sessionId,
        DateTime audienceCutoffAt,
        int deliveryPolicyId,
        NotificationFanoutAudienceCursor? cursor,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || eventId == Guid.Empty || audienceCutoffAt.Kind != DateTimeKind.Utc || batchSize <= 0)
        {
            return [];
        }

        var audience = dbContext.RegistrationOrders
            .AsNoTracking()
            .Where(order => order.TenantId == tenantId
                && order.EventId == eventId
                && order.AccountUserId != null
                && order.CreatedAt <= audienceCutoffAt
                && order.RegistrationOrderStatusId != (int)RegistrationOrderStatusEnum.Expired
                && order.RegistrationOrderStatusId != (int)RegistrationOrderStatusEnum.Cancelled
                && order.RegistrationOrderStatusId != (int)RegistrationOrderStatusEnum.Rejected)
            .GroupBy(order => order.AccountUserId!.Value)
            .Select(group => new NotificationFanoutAudienceMember(group.Key, group.Min(order => order.CreatedAt)));

        if (cursor is { } value)
        {
            audience = audience.Where(member => member.FirstEligibleRegistrationCreatedAt > value.FirstEligibleRegistrationCreatedAt
                || member.FirstEligibleRegistrationCreatedAt == value.FirstEligibleRegistrationCreatedAt && member.UserId > value.UserId);
        }

        return await audience
            .OrderBy(member => member.FirstEligibleRegistrationCreatedAt)
            .ThenBy(member => member.UserId)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EventCapacityPool>> GetPoolsForUpdateAsync(
        IReadOnlyCollection<Guid> capacityPoolIds,
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        Guid[] orderedIds = capacityPoolIds.Distinct().Order().ToArray();
        if (orderedIds.Length == 0)
        {
            return [];
        }

        foreach (Guid capacityPoolId in orderedIds)
        {
            await RelationalEntityRowFence.AcquireAsync<EventCapacityPool>(
                dbContext,
                tenantId,
                pool => pool.Id,
                capacityPoolId,
                cancellationToken);
        }

        return await dbContext.EventCapacityPools
            .Include(pool => pool.CapacityHoldPolicy)
            .Where(pool => pool.TenantId == tenantId && pool.EventId == eventId && orderedIds.Contains(pool.Id))
            .OrderBy(pool => pool.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetAllocatedQuantityAsync(Guid capacityPoolId, Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.RegistrationInventoryHolds
            .Where(hold => hold.TenantId == tenantId
                && hold.CapacityPoolId == capacityPoolId
                && !hold.IsDeleted
                && (hold.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Active
                    || hold.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Consumed))
            .SumAsync(hold => (int?)hold.Quantity, cancellationToken) ?? 0;

    public async Task<IReadOnlyDictionary<Guid, RegistrationTicketLimitUsage>> GetTicketLimitUsageAsync(
        Guid eventId,
        Guid tenantId,
        Guid? accountUserId,
        string? verifiedContactNormalizedEmail,
        Guid? bookingPartyActorId,
        IReadOnlyCollection<Guid> ticketTypeIds,
        CancellationToken cancellationToken)
    {
        Guid[] ticketIds = ticketTypeIds.Distinct().ToArray();
        if (ticketIds.Length == 0)
        {
            return new Dictionary<Guid, RegistrationTicketLimitUsage>();
        }

        var usage = await (
            from line in dbContext.RegistrationOrderLines
            join order in dbContext.RegistrationOrders
                on new { line.TenantId, line.RegistrationOrderId } equals new { order.TenantId, RegistrationOrderId = order.Id }
            join pii in dbContext.RegistrationOrderPii
                on new { line.TenantId, line.RegistrationOrderId } equals new { pii.TenantId, pii.RegistrationOrderId } into piiRows
            from pii in piiRows.DefaultIfEmpty()
            where line.TenantId == tenantId
                  && order.EventId == eventId
                  && ticketIds.Contains(line.TicketTypeId)
                  && order.RegistrationOrderStatusId != (int)RegistrationOrderStatusEnum.Expired
                  && order.RegistrationOrderStatusId != (int)RegistrationOrderStatusEnum.Cancelled
                  && order.RegistrationOrderStatusId != (int)RegistrationOrderStatusEnum.Rejected
            group new { line, order, pii } by line.TicketTypeId into grouped
            select new RegistrationTicketLimitUsage(
                grouped.Key,
                accountUserId.HasValue
                    ? grouped.Where(value => value.order.AccountUserId == accountUserId).Sum(value => (int?)value.line.Quantity) ?? 0
                    : 0,
                verifiedContactNormalizedEmail != null
                    ? grouped.Where(value => value.pii != null && value.pii.NormalizedEmail == verifiedContactNormalizedEmail).Sum(value => (int?)value.line.Quantity) ?? 0
                    : 0,
                bookingPartyActorId.HasValue
                    ? grouped.Where(value => value.order.PurchaserActorId == bookingPartyActorId).Sum(value => (int?)value.line.Quantity) ?? 0
                    : 0))
            .ToListAsync(cancellationToken);

        return usage.ToDictionary(item => item.TicketTypeId);
    }

    public async Task AddOrderWithHoldsAsync(
        RegistrationOrder order,
        IReadOnlyCollection<RegistrationInventoryHold> holds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(holds);
        if (holds.Any(hold => hold.RegistrationOrderId != order.Id || hold.TenantId != order.TenantId))
        {
            throw new ArgumentException("Inventory holds must belong to the supplied order.", nameof(holds));
        }

        await dbContext.RegistrationOrders.AddAsync(order, cancellationToken);
        await dbContext.RegistrationInventoryHolds.AddRangeAsync(holds, cancellationToken);
    }

    public async Task<RegistrationInventoryReservationResult> ReserveNonTimedHoldsAsync(
        Guid eventId,
        Guid tenantId,
        IReadOnlyCollection<RegistrationInventoryReservation> reservations,
        bool approvalGranted,
        DateTime utcNow,
        CancellationToken cancellationToken) =>
        await ReserveHoldsAsync(eventId, tenantId, reservations, approvalGranted, includeTimedHolds: false, utcNow, cancellationToken);

    public async Task<RegistrationInventoryReservationResult> ReserveRecoveredHoldsAsync(
        Guid eventId,
        Guid tenantId,
        IReadOnlyCollection<RegistrationInventoryReservation> reservations,
        DateTime utcNow,
        CancellationToken cancellationToken) =>
        await ReserveHoldsAsync(eventId, tenantId, reservations, approvalGranted: true, includeTimedHolds: true, utcNow, cancellationToken);

    public async Task<IReadOnlyList<RegistrationInventoryHold>> GetExpiredActiveHoldsAsync(
        DateTime utcNow,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (utcNow.Kind != DateTimeKind.Utc || batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        return await dbContext.RegistrationInventoryHolds
            .IgnoreQueryFilters([QueryFilterNames.Tenant])
            .AsNoTracking()
            .Where(hold => hold.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Active && hold.ExpiresAt <= utcNow)
            .OrderBy(hold => hold.ExpiresAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RegistrationHoldExpiryRecoveryTarget>> GetHoldExpiryRecoveryTargetsAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        return await dbContext.RegistrationOrders
            .IgnoreQueryFilters([QueryFilterNames.Tenant])
            .AsNoTracking()
            .Where(order => order.RegistrationOrderStatusId == (int)RegistrationOrderStatusEnum.NeedsReconciliation)
            .OrderBy(order => order.UpdatedAt)
            .Take(batchSize)
            .Select(order => new RegistrationHoldExpiryRecoveryTarget(order.TenantId, order.Id))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> TryExpireDueHoldAsync(
        Guid holdId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        RegistrationInventoryTime.RequireUtc(utcNow);

        if (dbContext.Database.CurrentTransaction is not null)
        {
            return TryExpireDueHoldCoreAsync(holdId, utcNow, cancellationToken);
        }

        IExecutionStrategy strategy = dbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async token =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                token);
            bool expired = await TryExpireDueHoldCoreAsync(holdId, utcNow, token);
            if (expired)
            {
                await transaction.CommitAsync(token);
            }

            return expired;
        }, cancellationToken);
    }

    private async Task<bool> TryExpireDueHoldCoreAsync(
        Guid holdId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var holdOwner = await dbContext.RegistrationInventoryHolds
            .IgnoreQueryFilters([QueryFilterNames.Tenant])
            .AsNoTracking()
            .Where(hold => hold.Id == holdId &&
                           hold.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Active &&
                           hold.ExpiresAt <= utcNow &&
                           !hold.IsDeleted)
            .Select(hold => new { hold.TenantId, hold.RegistrationOrderId })
            .SingleOrDefaultAsync(cancellationToken);
        if (holdOwner is null)
        {
            return false;
        }

        await using IAsyncDisposable orderLease = await RelationalNamedLock.AcquireTransactionAsync(
            dbContext,
            $"registration-order:{holdOwner.TenantId:N}:{holdOwner.RegistrationOrderId:N}",
            cancellationToken);
        await using IAsyncDisposable holdLease = await RelationalNamedLock.AcquireTransactionAsync(
            dbContext,
            $"registration-inventory-hold-expiry:{holdId:N}",
            cancellationToken);

        RegistrationInventoryHold? hold = await dbContext.RegistrationInventoryHolds
            .IgnoreQueryFilters([QueryFilterNames.Tenant])
            .SingleOrDefaultAsync(candidate => candidate.Id == holdId && !candidate.IsDeleted, cancellationToken);
        RegistrationOrder? order = await dbContext.RegistrationOrders
            .IgnoreQueryFilters([QueryFilterNames.Tenant])
            .SingleOrDefaultAsync(
                candidate => candidate.TenantId == holdOwner.TenantId
                    && candidate.Id == holdOwner.RegistrationOrderId
                    && !candidate.IsDeleted,
                cancellationToken);
        if (hold is null || order is null ||
            hold.RegistrationOrderId != order.Id ||
            hold.TenantId != order.TenantId)
        {
            return false;
        }

        bool paymentOutcomeAmbiguous = await dbContext.PaymentAttempts
            .IgnoreQueryFilters([QueryFilterNames.Tenant])
            .AsNoTracking()
            .AnyAsync(attempt => attempt.TenantId == holdOwner.TenantId &&
                                 attempt.RegistrationOrderId == holdOwner.RegistrationOrderId &&
                                 attempt.PaymentAttemptStatusId != (int)PaymentAttemptStatusEnum.Failed &&
                                 attempt.PaymentAttemptStatusId != (int)PaymentAttemptStatusEnum.Cancelled,
                cancellationToken);
        if (paymentOutcomeAmbiguous)
        {
            return false;
        }

        RegistrationOrderStatusEnum status = (RegistrationOrderStatusEnum)order.RegistrationOrderStatusId;
        bool alreadyRecovering = status == RegistrationOrderStatusEnum.NeedsReconciliation;
        if ((!alreadyRecovering && !order.CanTransitionTo(RegistrationOrderStatusEnum.NeedsReconciliation)) ||
            !hold.TryExpire(utcNow))
        {
            return false;
        }

        if (!alreadyRecovering && !order.TryBeginHoldExpiryRecovery(utcNow))
        {
            throw new InvalidOperationException("Registration order recovery authority changed after its lock was acquired.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    private async Task AcquireOrderLockIfTransactionalAsync(Guid tenantId, Guid orderId, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            await RelationalNamedLock.AcquireTransactionAsync(dbContext, $"registration-order:{tenantId:N}:{orderId:N}", cancellationToken);
        }
    }

    public async Task<bool> TryConsumeActiveHoldAsync(Guid holdId, DateTime utcNow, CancellationToken cancellationToken)
    {
        RegistrationInventoryTime.RequireUtc(utcNow);
        int affected = await dbContext.RegistrationInventoryHolds
            .IgnoreQueryFilters([QueryFilterNames.Tenant])
            .Where(hold => hold.Id == holdId
                && hold.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Active
                && hold.ExpiresAt > utcNow
                && !hold.IsDeleted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    hold => hold.RegistrationInventoryHoldStatusId,
                    (int)RegistrationInventoryHoldStatusEnum.Consumed)
                .SetProperty(hold => hold.ConsumedAt, utcNow)
                .SetProperty(hold => hold.UpdatedAt, utcNow)
                .SetProperty(hold => hold.ConcurrencyStamp, Guid.CreateVersion7()), cancellationToken);
        return affected == 1;
    }

    public async Task<int> TryConsumeActiveHoldsForOrderAsync(
        Guid orderId,
        Guid tenantId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        RegistrationInventoryTime.RequireUtc(utcNow);
        return await dbContext.RegistrationInventoryHolds
            .Where(hold => hold.RegistrationOrderId == orderId
                && hold.TenantId == tenantId
                && hold.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Active
                && hold.ExpiresAt > utcNow)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(hold => hold.RegistrationInventoryHoldStatusId, (int)RegistrationInventoryHoldStatusEnum.Consumed)
                .SetProperty(hold => hold.ConsumedAt, utcNow)
                .SetProperty(hold => hold.UpdatedAt, utcNow)
                .SetProperty(hold => hold.ConcurrencyStamp, Guid.CreateVersion7()), cancellationToken);
    }

    public async Task<bool> TryReleaseActiveHoldAsync(
        Guid holdId,
        RegistrationInventoryHoldStatusEnum outcome,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (outcome is not (RegistrationInventoryHoldStatusEnum.Released or RegistrationInventoryHoldStatusEnum.Cancelled))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        RegistrationInventoryTime.RequireUtc(utcNow);
        int affected = await dbContext.RegistrationInventoryHolds
            .IgnoreQueryFilters([QueryFilterNames.Tenant])
            .Where(hold => hold.Id == holdId
                && hold.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Active
                && !hold.IsDeleted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(hold => hold.RegistrationInventoryHoldStatusId, (int)outcome)
                .SetProperty(hold => hold.ReleasedAt, utcNow)
                .SetProperty(hold => hold.UpdatedAt, utcNow)
                .SetProperty(hold => hold.ConcurrencyStamp, Guid.CreateVersion7()), cancellationToken);
        return affected == 1;
    }

    public async Task<int> TryReleaseActiveHoldsForOrderAsync(
        Guid orderId,
        Guid tenantId,
        RegistrationInventoryHoldStatusEnum outcome,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (outcome is not (RegistrationInventoryHoldStatusEnum.Released or RegistrationInventoryHoldStatusEnum.Cancelled))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        RegistrationInventoryTime.RequireUtc(utcNow);
        return await dbContext.RegistrationInventoryHolds
            .Where(hold => hold.RegistrationOrderId == orderId
                && hold.TenantId == tenantId
                && hold.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Active)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(hold => hold.RegistrationInventoryHoldStatusId, (int)outcome)
                .SetProperty(hold => hold.ReleasedAt, utcNow)
                .SetProperty(hold => hold.UpdatedAt, utcNow)
                .SetProperty(hold => hold.ConcurrencyStamp, Guid.CreateVersion7()), cancellationToken);
    }
    public async Task AddEventRegistrationsAsync(
        IReadOnlyCollection<EventRegistration> registrations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        if (registrations.Count == 0)
        {
            return;
        }

        if (registrations.Any(registration => registration.Id == Guid.Empty
            || registration.RegistrationOrderId is null
            || registration.RegistrationOrderLineId is null
            || registration.TicketTypeEntitlementId is null
            || registration.EntitlementOrdinal is null))
        {
            throw new ArgumentException("Order admissions require complete order-line entitlement identity.", nameof(registrations));
        }

        await dbContext.EventRegistrations.AddRangeAsync(registrations, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    private async Task<Guid[]> GetUnavailablePoolIdsAsync(
        IReadOnlyCollection<RegistrationInventoryReservation> reservations,
        IReadOnlyDictionary<Guid, EventCapacityPool> poolsById,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var unavailable = new List<Guid>();
        foreach (IGrouping<Guid, RegistrationInventoryReservation> group in reservations
                     .GroupBy(reservation => reservation.CapacityPoolId)
                     .OrderBy(group => group.Key))
        {
            EventCapacityPool pool = poolsById[group.Key];
            if (pool.MaximumQuantity is null || pool.CapacityOversellPolicyId == (int)CapacityOversellPolicyEnum.Allow)
            {
                continue;
            }

            int allocated = await GetAllocatedQuantityAsync(pool.Id, tenantId, cancellationToken);
            int requested = checked(group.Sum(reservation => reservation.Quantity));
            if (allocated > pool.MaximumQuantity.Value - requested)
            {
                unavailable.Add(pool.Id);
            }
        }

        return unavailable.ToArray();
    }

    private async Task<RegistrationInventoryReservationResult> ReserveHoldsAsync(
        Guid eventId,
        Guid tenantId,
        IReadOnlyCollection<RegistrationInventoryReservation> reservations,
        bool approvalGranted,
        bool includeTimedHolds,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        RegistrationInventoryTime.RequireUtc(utcNow);
        ArgumentNullException.ThrowIfNull(reservations);
        if (eventId == Guid.Empty || tenantId == Guid.Empty || reservations.Any(reservation =>
                reservation.HoldId == Guid.Empty ||
                reservation.RegistrationOrderId == Guid.Empty ||
                reservation.CapacityPoolId == Guid.Empty ||
                reservation.TicketTypeId == Guid.Empty ||
                reservation.Quantity <= 0))
        {
            throw new ArgumentException("Complete positive registration inventory reservations are required.", nameof(reservations));
        }

        if (reservations.Count == 0)
        {
            return new RegistrationInventoryReservationResult(Reserved: true, RequiresApproval: false, ShouldWaitlist: false);
        }

        IReadOnlyList<EventCapacityPool> pools = await GetPoolsForUpdateAsync(
            reservations.Select(reservation => reservation.CapacityPoolId).ToArray(),
            eventId,
            tenantId,
            cancellationToken);
        var poolsById = pools.ToDictionary(pool => pool.Id);
        if (poolsById.Count != reservations.Select(reservation => reservation.CapacityPoolId).Distinct().Count() ||
            poolsById.Values.Any(pool => !pool.IsActive || !Enum.IsDefined((CapacityHoldPolicyEnum)pool.CapacityHoldPolicyId)))
        {
            throw new InvalidOperationException("Registration capacity pools are unavailable.");
        }

        if (!approvalGranted && reservations.Any(reservation =>
                (CapacityHoldPolicyEnum)poolsById[reservation.CapacityPoolId].CapacityHoldPolicyId == CapacityHoldPolicyEnum.ApprovalNoHold))
        {
            return new RegistrationInventoryReservationResult(Reserved: false, RequiresApproval: true, ShouldWaitlist: false);
        }

        RegistrationInventoryReservation[] reservationsToCreate = reservations
            .Where(reservation => includeTimedHolds ||
                (CapacityHoldPolicyEnum)poolsById[reservation.CapacityPoolId].CapacityHoldPolicyId != CapacityHoldPolicyEnum.TimedHoldOnSelection)
            .OrderBy(reservation => reservation.CapacityPoolId)
            .ThenBy(reservation => reservation.TicketTypeId)
            .ToArray();
        Guid[] unavailablePoolIds = await GetUnavailablePoolIdsAsync(reservationsToCreate, poolsById, tenantId, cancellationToken);
        if (unavailablePoolIds.Length > 0)
        {
            bool shouldWaitlist = unavailablePoolIds.All(poolId =>
                (CapacityHoldPolicyEnum)poolsById[poolId].CapacityHoldPolicyId == CapacityHoldPolicyEnum.WaitlistWhenFull);
            return new RegistrationInventoryReservationResult(Reserved: false, RequiresApproval: false, ShouldWaitlist: shouldWaitlist);
        }

        if (reservationsToCreate.Length == 0)
        {
            return new RegistrationInventoryReservationResult(Reserved: true, RequiresApproval: false, ShouldWaitlist: false);
        }

        RegistrationInventoryHold[] holds = reservationsToCreate
            .Select(reservation =>
            {
                EventCapacityPool pool = poolsById[reservation.CapacityPoolId];
                return RegistrationInventoryHold.Create(
                    reservation.HoldId,
                    reservation.RegistrationOrderId,
                    pool.Id,
                    reservation.TicketTypeId,
                    tenantId,
                    reservation.Quantity,
                    utcNow,
                    utcNow.AddSeconds(pool.HoldDurationSeconds));
            })
            .ToArray();
        await dbContext.RegistrationInventoryHolds.AddRangeAsync(holds, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new RegistrationInventoryReservationResult(Reserved: true, RequiresApproval: false, ShouldWaitlist: false);
    }
}
