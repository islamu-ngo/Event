using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IRegistrationParticipantRepository
{
    Task<RegistrationParticipant?> GetParticipantAsync(
        Guid participantId,
        Guid registrationOrderId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<RegistrationParticipant?> GetParticipantForUpdateAsync(
        Guid participantId,
        Guid registrationOrderId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RegistrationParticipant>> GetParticipantsByOrderAsync(
        Guid registrationOrderId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RegistrationTicketAssignment>> GetAssignmentsWithParticipantsByOrderAsync(
        Guid registrationOrderId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RegistrationTicketAssignment>> GetAssignmentsForUpdateByOrderAsync(
        Guid registrationOrderId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EventRegistration>> GetAdmissionsForUpdateAsync(
        Guid registrationOrderId,
        Guid registrationOrderLineId,
        int ordinal,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<bool> HasCompanyCsvAmendmentAsync(
        Guid registrationOrderId,
        Guid tenantId,
        string lineageKey,
        CancellationToken cancellationToken);

    Task AddParticipantAsync(RegistrationParticipant participant, CancellationToken cancellationToken);

    Task AddAssignmentsAsync(
        IReadOnlyCollection<RegistrationTicketAssignment> assignments,
        CancellationToken cancellationToken);

    Task AddAmendmentsAsync(
        IReadOnlyCollection<RegistrationAmendment> amendments,
        CancellationToken cancellationToken);

    Task AddParticipantsAsync(
        IReadOnlyCollection<RegistrationParticipant> participants,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
