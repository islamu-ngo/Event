using Explore.Application.Contracts.Admissions;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public sealed class AdmissionRecoveryIdentityResolver(ExploreDbContext dbContext) :
    IAdmissionRecoveryIdentityResolver
{
    public async Task<AdmissionRecoveryIdentityResult> FindAsync(
        AdmissionRecoveryRequest request,
        CancellationToken cancellationToken)
    {
        string normalizedIdentity = request.NormalizedIdentity.Trim().ToUpperInvariant();
        int activeStatus = (int)AdmissionTicketStatusEnum.Active;
        int suspendedStatus = (int)AdmissionTicketStatusEnum.Suspended;
        Guid[] ticketIds = await (
                from pii in dbContext.RegistrationOrderPii.AsNoTracking()
                join ticket in dbContext.AdmissionTickets.AsNoTracking()
                    on new { pii.TenantId, pii.RegistrationOrderId }
                    equals new { ticket.TenantId, ticket.RegistrationOrderId }
                where pii.TenantId == request.TenantId &&
                    pii.IsEmailVerified &&
                    pii.NormalizedEmail == normalizedIdentity &&
                    (ticket.AdmissionTicketStatusId == activeStatus ||
                        ticket.AdmissionTicketStatusId == suspendedStatus)
                orderby ticket.CreatedAt descending, ticket.Id
                select ticket.Id)
            .Distinct()
            .Take(1)
            .ToArrayAsync(cancellationToken);
        return new AdmissionRecoveryIdentityResult(
            request.TenantId,
            Guid.CreateVersion7(),
            ticketIds.Length > 0,
            ticketIds);
    }
}
