using Explore.Application.Contracts.Admissions;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class AdmissionRevocationRepository(ExploreDbContext dbContext)
    : IAdmissionRevocationRepository, IAdmissionEventCancellationRepository
{
    public async Task<AdmissionRevocationContext?> LoadAsync(
        AdmissionRevocationRequest request,
        CancellationToken cancellationToken)
    {
        await RelationalEntityRowFence.AcquireAsync<RegistrationOrder>(
            dbContext,
            request.TenantId,
            order => order.Id,
            request.RegistrationOrderId,
            cancellationToken);
        IQueryable<RegistrationOrder> orders = dbContext.RegistrationOrders;
        RegistrationOrder? order = await orders.SingleOrDefaultAsync(value =>
            value.TenantId == request.TenantId &&
            value.Id == request.RegistrationOrderId,
            cancellationToken);
        if (order is null)
        {
            return null;
        }

        await RelationalEntityRowFence.AcquireAsync<AdmissionTicket>(
            dbContext,
            request.TenantId,
            ticket => ticket.RegistrationOrderId,
            request.RegistrationOrderId,
            cancellationToken);
        IQueryable<AdmissionTicket> tickets = dbContext.AdmissionTickets;

        AdmissionTicket[] current = await tickets
            .Include(ticket => ticket.Credentials)
            .Where(ticket => ticket.TenantId == request.TenantId &&
                             ticket.RegistrationOrderId == request.RegistrationOrderId)
            .OrderBy(ticket => ticket.Id)
            .ToArrayAsync(cancellationToken);
        return new AdmissionRevocationContext(
            request.TenantId,
            request.RegistrationOrderId,
            current);
    }

    public async Task<AdmissionRevocationResult> ApplyAsync(
        AdmissionRevocationPersistenceRequest request,
        CancellationToken cancellationToken)
    {
        HashSet<Guid> revoked = request.RevokedTicketIds.ToHashSet();
        HashSet<Guid> preserved = request.PreservedTicketIds.ToHashSet();
        if (revoked.Overlaps(preserved))
        {
            throw new InvalidOperationException("Admission revocation partitions must be disjoint.");
        }

        Guid[] trackedIds = dbContext.ChangeTracker.Entries<AdmissionTicket>()
            .Where(entry => entry.Entity.TenantId == request.TenantId &&
                            entry.Entity.RegistrationOrderId == request.RegistrationOrderId)
            .Select(entry => entry.Entity.Id)
            .Order()
            .ToArray();
        if (!trackedIds.SequenceEqual(revoked.Concat(preserved).Order()))
        {
            throw new InvalidOperationException("Admission revocation partitions must cover the locked order tickets.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new AdmissionRevocationResult(
            AdmissionRevocationOutcome.Applied,
            request.RevokedTicketIds,
            request.PreservedTicketIds);
    }

    public async Task<IReadOnlyList<Guid>> ListRevocableOrderIdsAsync(
        Guid tenantId,
        Guid eventId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || eventId == Guid.Empty ||
            batchSize is < 1 or > 1000)
        {
            throw new ArgumentException("Admission cancellation batch is invalid.");
        }

        int[] terminalStatuses =
        [
            (int)AdmissionTicketStatusEnum.Revoked,
            (int)AdmissionTicketStatusEnum.Cancelled,
            (int)AdmissionTicketStatusEnum.Transferred,
            (int)AdmissionTicketStatusEnum.Expired
        ];
        return await dbContext.AdmissionTickets
            .AsNoTracking()
            .Where(ticket => ticket.TenantId == tenantId &&
                             ticket.EventId == eventId &&
                             !terminalStatuses.Contains(ticket.AdmissionTicketStatusId))
            .Select(ticket => ticket.RegistrationOrderId)
            .Distinct()
            .Order()
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);
    }

    public async Task ScheduleContinuationAsync(
        Guid sourceMessageId,
        Guid tenantId,
        Guid eventId,
        DateTime createdAt,
        CancellationToken cancellationToken)
    {
        bool continuationExists = await dbContext.OutboxMessages
            .AsNoTracking()
            .AnyAsync(message =>
                message.Id != sourceMessageId &&
                message.AggregateId == eventId &&
                message.EventType ==
                    AdmissionRevocationOutboxMessageFactory.EventCancellationRequested &&
                (message.Status == OutboxMessageStatus.Pending ||
                 message.Status == OutboxMessageStatus.Processing),
                cancellationToken);
        if (continuationExists)
        {
            return;
        }

        await dbContext.OutboxMessages.AddAsync(
            AdmissionRevocationOutboxMessageFactory.CreateEventCancellation(
                tenantId, eventId, createdAt),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
