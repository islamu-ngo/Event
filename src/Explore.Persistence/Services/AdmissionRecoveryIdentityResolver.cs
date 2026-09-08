using Explore.Application.Contracts.Admissions;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public sealed class AdmissionRecoveryIdentityResolver(ExploreDbContext dbContext, TimeProvider? timeProvider = null) :
    IAdmissionRecoveryIdentityResolver
{
    public async Task<AdmissionRecoveryIdentityResult> FindAsync(
        AdmissionRecoveryRequest request,
        CancellationToken cancellationToken)
    {
        string normalizedIdentity = request.NormalizedIdentity.Trim().ToUpperInvariant();
        int activeStatus = (int)AdmissionTicketStatusEnum.Active;
        int suspendedStatus = (int)AdmissionTicketStatusEnum.Suspended;
        var candidates = await (
                from pii in dbContext.RegistrationOrderPii.AsNoTracking()
                join order in dbContext.RegistrationOrders.AsNoTracking()
                    on new { pii.TenantId, Id = pii.RegistrationOrderId }
                    equals new { order.TenantId, order.Id }
                join ticket in dbContext.AdmissionTickets.AsNoTracking()
                    on new { pii.TenantId, pii.RegistrationOrderId }
                    equals new { ticket.TenantId, ticket.RegistrationOrderId }
                where pii.TenantId == request.TenantId &&
                    pii.IsEmailVerified &&
                    pii.NormalizedEmail == normalizedIdentity &&
                    (ticket.AdmissionTicketStatusId == activeStatus ||
                        ticket.AdmissionTicketStatusId == suspendedStatus)
                orderby ticket.CreatedAt descending, ticket.Id
                select new { TicketId = ticket.Id, Order = order, Pii = pii })
            .ToArrayAsync(cancellationToken);
        DateTime utcNow = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        Guid[] ticketIds = candidates
            .Where(candidate => AnonymousRegistrationRetentionPolicy.CanDisclose(
                candidate.Order, candidate.Pii.RetentionUntil, utcNow))
            .Select(candidate => candidate.TicketId)
            .Distinct().Take(1).ToArray();
        return new AdmissionRecoveryIdentityResult(
            request.TenantId,
            Guid.CreateVersion7(),
            ticketIds.Length > 0,
            ticketIds);
    }
}
