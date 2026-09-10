
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class AnonymousCancellationRepository(
    ExploreDbContext dbContext,
    IParticipantAdmissionEligibilityRepository readiness,
    IRegistrationInventoryRepository inventory) : IAnonymousCancellationRepository
{
    public async Task<AnonymousCancellationContext> LoadInCurrentTransactionAsync(
        Guid tenantId, Guid eventId, Guid orderId, CancellationToken cancellationToken)
    {
        // P09 owns the order/event fences. Reacquiring only the order is safe; never take a
        // finalization-effect fence here (issuance holds effect -> order -> event -> assignment).
        await RelationalEntityRowFence.AcquireAsync<RegistrationOrder>(
            dbContext, tenantId, order => order.Id, orderId, cancellationToken);
        var tracked = dbContext.RegistrationOrders.Local.SingleOrDefault(order => order.Id == orderId && order.TenantId == tenantId);
        if (tracked is not null)
            await dbContext.Entry(tracked).ReloadAsync(cancellationToken);
        var order = await dbContext.RegistrationOrders
            .Include(value => value.Lines).Include(value => value.AddOnLines).Include(value => value.PlatformContribution)
            .SingleAsync(value => value.TenantId == tenantId && value.EventId == eventId && value.Id == orderId, cancellationToken);

        // No active-only credential/ticket predicate: terminal and transferred lineage can retain attendance.
        var lineage = await dbContext.AdmissionTickets.AsNoTracking()
            .Where(ticket => ticket.TenantId == tenantId && ticket.RegistrationOrderId == orderId)
            .Select(ticket => new { ticket.Id, ticket.RegistrationTicketAssignmentId }).ToArrayAsync(cancellationToken);
        Guid[] assignments = await dbContext.RegistrationTicketAssignments.IncludeDeleted().AsNoTracking()
            .Where(value => value.TenantId == tenantId && value.RegistrationOrderId == orderId)
            .Select(value => value.Id).ToArrayAsync(cancellationToken);
        foreach (Guid assignmentId in assignments.Concat(lineage.Select(value => value.RegistrationTicketAssignmentId)).Distinct().Order())
            await readiness.LoadForUpdateAsync(tenantId, assignmentId, cancellationToken);

        Guid[] ticketIds = lineage.Select(value => value.Id).Order().ToArray();
        foreach (Guid ticketId in ticketIds)
            await RelationalEntityRowFence.AcquireAsync<AdmissionTicket>(
                dbContext, tenantId, ticket => ticket.Id, ticketId, cancellationToken);

        // Event targets include currently empty scopes. Ticket fences exclude first-ever check-in too.
        Guid[] targetIds = await dbContext.AdmissionTargets.AsNoTracking()
            .Where(target => target.TenantId == tenantId && target.EventId == eventId)
            .Select(target => target.Id).ToArrayAsync(cancellationToken);
        foreach (Guid targetId in targetIds.Order())
            await RelationalEntityRowFence.AcquireAsync<AdmissionTarget>(
                dbContext, tenantId, target => target.Id, targetId, cancellationToken);

        var tickets = await dbContext.AdmissionTickets.Include(ticket => ticket.Credentials)
            .Where(ticket => ticket.TenantId == tenantId && ticket.RegistrationOrderId == orderId)
            .OrderBy(ticket => ticket.Id).ToArrayAsync(cancellationToken);
        foreach (var ticket in tickets)
        {
            await dbContext.Entry(ticket).ReloadAsync(cancellationToken);
            foreach (var credential in ticket.Credentials)
                await dbContext.Entry(credential).ReloadAsync(cancellationToken);
        }
        if (!tickets.Select(ticket => ticket.Id).SequenceEqual(ticketIds))
            throw new InvalidOperationException("Order ticket lineage changed while issuance was fenced.");

        bool attended = await dbContext.AdmissionCheckInStates.AsNoTracking()
            .AnyAsync(state => state.TenantId == tenantId && ticketIds.Contains(state.AdmissionTicketId) &&
                (state.EntryCount > 0 || state.ActiveCheckInEventId != null || state.LastSequence > 0), cancellationToken) ||
            await dbContext.AdmissionCheckInEvents.AsNoTracking()
                .AnyAsync(fact => fact.TenantId == tenantId && ticketIds.Contains(fact.AdmissionTicketId), cancellationToken);
        var evidence = new AnonymousCancellationEvidence(
            await dbContext.PaidOrderAcceptanceSnapshots.AsNoTracking()
                .AnyAsync(value => value.TenantId == tenantId && value.RegistrationOrderId == orderId, cancellationToken),
            await dbContext.PaymentAttempts.AsNoTracking()
                .AnyAsync(value => value.TenantId == tenantId && value.RegistrationOrderId == orderId, cancellationToken),
            await dbContext.PaymentSucceededObservations.AsNoTracking()
                .AnyAsync(value => value.TenantId == tenantId && value.RegistrationOrderId == orderId, cancellationToken),
            attended);
        var holds = await dbContext.RegistrationInventoryHolds.IncludeDeleted().AsNoTracking()
            .Where(hold => hold.TenantId == tenantId && hold.RegistrationOrderId == orderId)
            .OrderBy(hold => hold.Id).ToArrayAsync(cancellationToken);
        return new(order, evidence, tickets, holds);
    }

    public async Task<IReadOnlyList<Guid>> ReleaseConsumedInCurrentTransactionAsync(
        AnonymousCancellationContext context, DateTime releasedAt, CancellationToken cancellationToken)
    {
        Guid[] poolIds = context.ConsumedHolds.Select(hold => hold.CapacityPoolId).Distinct().Order().ToArray();
        var pools = await inventory.GetPoolsForUpdateAsync(poolIds, context.Order.EventId, context.Order.TenantId, cancellationToken);
        if (!pools.Select(pool => pool.Id).Order().SequenceEqual(poolIds))
            throw new InvalidOperationException("Cancellation capacity pools must match the exact order holds.");
        var released = new List<Guid>();
        foreach (var hold in context.ConsumedHolds.OrderBy(value => value.Id))
        {
            Guid expectedStamp = hold.ConcurrencyStamp;
            if (!hold.TryReleaseConsumedForAnonymousCancellation(context.Order, releasedAt))
                throw new InvalidOperationException("Cancellation must release every exact consumed hold.");
            int affected = await dbContext.RegistrationInventoryHolds
                .Where(value => value.TenantId == context.Order.TenantId && value.RegistrationOrderId == context.Order.Id &&
                    value.Id == hold.Id && value.CapacityPoolId == hold.CapacityPoolId && value.TicketTypeId == hold.TicketTypeId &&
                    value.Quantity == hold.Quantity && value.ConsumedAt == hold.ConsumedAt && value.ReleasedAt == null &&
                    value.ConcurrencyStamp == expectedStamp && value.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Consumed)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(value => value.RegistrationInventoryHoldStatusId, hold.RegistrationInventoryHoldStatusId)
                    .SetProperty(value => value.ReleasedAt, hold.ReleasedAt)
                    .SetProperty(value => value.UpdatedAt, hold.UpdatedAt)
                    .SetProperty(value => value.ConcurrencyStamp, hold.ConcurrencyStamp), cancellationToken);
            if (affected != 1)
                throw new InvalidOperationException("The exact consumed hold changed before cancellation release.");
            released.Add(hold.Id);
        }
        return released;
    }
}
