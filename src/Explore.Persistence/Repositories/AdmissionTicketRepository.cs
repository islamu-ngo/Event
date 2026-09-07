using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class AdmissionTicketRepository(ExploreDbContext dbContext) :
    Explore.Application.Contracts.Admissions.IAdmissionTicketRecoveryRepository
{
    public async Task<AdmissionTicket?> GetByAssignmentAsync(
        Guid tenantId,
        Guid registrationTicketAssignmentId,
        CancellationToken cancellationToken) => await dbContext.AdmissionTickets
        .Include(ticket => ticket.Credentials)
        .SingleOrDefaultAsync(ticket => ticket.TenantId == tenantId &&
            ticket.RegistrationTicketAssignmentId == registrationTicketAssignmentId,
            cancellationToken);

    public async Task<AdmissionTicket?> GetByCredentialDigestAsync(
        Guid tenantId,
        int lookupKeyVersion,
        string lookupDigest,
        CancellationToken cancellationToken) => await dbContext.AdmissionTickets
        .AsNoTracking()
        .Include(ticket => ticket.Credentials)
        .SingleOrDefaultAsync(ticket => ticket.TenantId == tenantId && ticket.Credentials.Any(credential =>
            credential.LookupKeyVersion == lookupKeyVersion &&
            credential.LookupDigest == lookupDigest &&
            credential.AdmissionTicketCredentialStatusId == (int)AdmissionTicketCredentialStatusEnum.Active),
            cancellationToken);

    public async Task<AdmissionTicket?> GetByIdForUpdateAsync(
        Guid tenantId,
        Guid admissionTicketId,
        CancellationToken cancellationToken)
    {
        Guid? assignmentId =
            await dbContext.AdmissionTickets
                .AsNoTracking()
                .Where(ticket =>
                    ticket.TenantId == tenantId
                    && ticket.Id == admissionTicketId)
                .Select(ticket =>
                    (Guid?)ticket
                        .RegistrationTicketAssignmentId)
                .SingleOrDefaultAsync(cancellationToken);
        if (!assignmentId.HasValue)
        {
            return null;
        }

        await RelationalEntityRowFence
            .AcquireAsync<RegistrationTicketAssignment>(
                dbContext,
                tenantId,
                assignment => assignment.Id,
                assignmentId.Value,
                cancellationToken);
        await RelationalEntityRowFence
            .AcquireAsync<ParticipantAdmissionEligibility>(
                dbContext,
                tenantId,
                eligibility => eligibility.RegistrationTicketAssignmentId,
                assignmentId.Value,
                cancellationToken);
        await RelationalEntityRowFence.AcquireAsync<AdmissionTicket>(
            dbContext,
            tenantId,
            ticket => ticket.Id,
            admissionTicketId,
            cancellationToken);
        return await dbContext.AdmissionTickets
            .Include(ticket => ticket.Credentials)
            .SingleOrDefaultAsync(
                ticket => ticket.TenantId == tenantId && ticket.Id == admissionTicketId,
                cancellationToken);
    }

    public async Task<AdmissionTicket> AddAsync(AdmissionTicket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        await dbContext.AdmissionTickets.AddAsync(ticket, cancellationToken);
        return ticket;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        await dbContext.SaveChangesAsync(cancellationToken);

    public Task<AdmissionTicket?> GetForUpdateAsync(
        Guid tenantId,
        Guid admissionTicketId,
        CancellationToken cancellationToken) =>
        GetByIdForUpdateAsync(tenantId, admissionTicketId, cancellationToken);

    async Task Explore.Application.Contracts.Admissions.IAdmissionTicketRecoveryRepository.SaveChangesAsync(
        CancellationToken cancellationToken) =>
        _ = await SaveChangesAsync(cancellationToken);
}
