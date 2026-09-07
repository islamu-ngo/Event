using Explore.Application.Contracts.Admissions;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class AdmissionTicketAccountRepository(ExploreDbContext dbContext) :
    IAdmissionTicketAccountRepository
{
    public async Task<IReadOnlyList<AdmissionTicket>> ListCurrentAsync(
        Guid tenantId,
        Guid accountUserId,
        CancellationToken cancellationToken) =>
        await (
                from ticket in dbContext.AdmissionTickets.AsNoTracking()
                join order in dbContext.RegistrationOrders.AsNoTracking()
                    on new { ticket.TenantId, ticket.RegistrationOrderId }
                    equals new { order.TenantId, RegistrationOrderId = order.Id }
                where ticket.TenantId == tenantId &&
                    order.AccountUserId == accountUserId &&
                    ticket.AdmissionTicketStatusId == (int)AdmissionTicketStatusEnum.Active
                orderby ticket.CreatedAt descending, ticket.Id
                select ticket)
            .ToArrayAsync(cancellationToken);

    public async Task<AdmissionTicket?> GetOwnedAsync(
        Guid tenantId,
        Guid accountUserId,
        Guid admissionTicketId,
        CancellationToken cancellationToken) =>
        await (
                from ticket in dbContext.AdmissionTickets.AsNoTracking()
                join order in dbContext.RegistrationOrders.AsNoTracking()
                    on new { ticket.TenantId, ticket.RegistrationOrderId }
                    equals new { order.TenantId, RegistrationOrderId = order.Id }
                where ticket.TenantId == tenantId &&
                    ticket.Id == admissionTicketId &&
                    order.AccountUserId == accountUserId
                select ticket)
            .SingleOrDefaultAsync(cancellationToken);
}
