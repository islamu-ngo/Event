using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class RegistrationParticipantRepository(ExploreDbContext dbContext) : IRegistrationParticipantRepository
{
    public Task<RegistrationParticipant?> GetParticipantAsync(
        Guid participantId,
        Guid registrationOrderId,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        dbContext.RegistrationParticipants
            .AsNoTracking()
            .SingleOrDefaultAsync(
                participant =>
                    participant.Id == participantId
                    && participant.RegistrationOrderId ==
                    registrationOrderId
                    && participant.TenantId == tenantId,
                cancellationToken);

    public Task<RegistrationParticipant?> GetParticipantForUpdateAsync(
        Guid participantId,
        Guid registrationOrderId,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        dbContext.RegistrationParticipants
            .Include(participant => participant.Pii)
            .Include(participant => participant.GuardianParticipant)
            .FirstOrDefaultAsync(participant => participant.Id == participantId &&
                participant.RegistrationOrderId == registrationOrderId && participant.TenantId == tenantId, cancellationToken);

    public async Task<IReadOnlyList<RegistrationParticipant>> GetParticipantsByOrderAsync(
        Guid registrationOrderId,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        await dbContext.RegistrationParticipants
            .AsNoTracking()
            .Include(participant => participant.Pii)
            .Where(participant => participant.RegistrationOrderId == registrationOrderId && participant.TenantId == tenantId)
            .OrderBy(participant => participant.CreatedAt)
            .ThenBy(participant => participant.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RegistrationTicketAssignment>> GetAssignmentsWithParticipantsByOrderAsync(
        Guid registrationOrderId,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        await dbContext.RegistrationTicketAssignments
            .AsNoTracking()
            .Include(assignment => assignment.Participant)
            .Where(assignment => assignment.RegistrationOrderId == registrationOrderId && assignment.TenantId == tenantId)
            .OrderBy(assignment => assignment.RegistrationOrderLineId)
            .ThenBy(assignment => assignment.Ordinal)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RegistrationTicketAssignment>> GetAssignmentsForUpdateByOrderAsync(
        Guid registrationOrderId,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        await dbContext.RegistrationTicketAssignments
            .Include(assignment => assignment.Participant)
                .ThenInclude(participant => participant!.Pii)
            .Where(assignment => assignment.RegistrationOrderId == registrationOrderId && assignment.TenantId == tenantId)
            .OrderBy(assignment => assignment.RegistrationOrderLineId)
            .ThenBy(assignment => assignment.Ordinal)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EventRegistration>> GetAdmissionsForUpdateAsync(
        Guid registrationOrderId,
        Guid registrationOrderLineId,
        int ordinal,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        await dbContext.EventRegistrations
            .Where(registration => registration.RegistrationOrderId == registrationOrderId &&
                registration.RegistrationOrderLineId == registrationOrderLineId &&
                registration.EntitlementOrdinal == ordinal && registration.TenantId == tenantId)
            .OrderBy(registration => registration.EventSessionId)
            .ToListAsync(cancellationToken);

    public Task<bool> HasCompanyCsvAmendmentAsync(
        Guid registrationOrderId,
        Guid tenantId,
        string lineageKey,
        CancellationToken cancellationToken) =>
        dbContext.RegistrationAmendments.AnyAsync(amendment => amendment.RegistrationOrderId == registrationOrderId &&
            amendment.TenantId == tenantId && amendment.Source == "company-csv" && amendment.LineageKey == lineageKey, cancellationToken);

    public Task AddParticipantAsync(RegistrationParticipant participant, CancellationToken cancellationToken) =>
        dbContext.RegistrationParticipants.AddAsync(participant, cancellationToken).AsTask();

    public async Task AddAssignmentsAsync(
        IReadOnlyCollection<RegistrationTicketAssignment> assignments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        await dbContext.RegistrationTicketAssignments.AddRangeAsync(assignments, cancellationToken);
    }

    public async Task AddAmendmentsAsync(IReadOnlyCollection<RegistrationAmendment> amendments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(amendments);
        if (amendments.Count > 0)
        {
            await dbContext.RegistrationAmendments.AddRangeAsync(amendments, cancellationToken);
        }
    }

    public async Task AddParticipantsAsync(
        IReadOnlyCollection<RegistrationParticipant> participants,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(participants);
        if (participants.Count == 0)
        {
            return;
        }

        Guid tenantId = participants.First().TenantId;
        Guid orderId = participants.First().RegistrationOrderId;
        if (participants.Any(participant => participant.Id == Guid.Empty ||
                participant.TenantId != tenantId ||
                participant.RegistrationOrderId != orderId ||
                participant.LinkedUserId is not null))
        {
            throw new ArgumentException("Participants must belong to one order and cannot be pre-linked to users.", nameof(participants));
        }

        await dbContext.RegistrationParticipants.AddRangeAsync(participants, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
