using Explore.Domain;

namespace Explore.Application.Contracts.Admissions;

public sealed record ParticipantAdmissionCompletionContext(
    ParticipantAdmissionEligibility Eligibility,
    RegistrationParticipant Participant,
    bool RequirementsComplete,
    Guid? SubjectConsentRecordId);

public interface IParticipantAdmissionReadinessAuthority
{
    Task<ParticipantAdmissionReadinessDecision?>
        EvaluateForUpdateAsync(
            Guid tenantId,
            Guid registrationTicketAssignmentId,
            bool orderConfirmed,
            bool paymentSatisfied,
            CancellationToken cancellationToken);
}

public interface IParticipantAdmissionEligibilityRepository :
    IParticipantAdmissionReadinessAuthority
{
    Task<ParticipantAdmissionEligibility?> GetAsync(
        Guid tenantId,
        Guid registrationTicketAssignmentId,
        CancellationToken cancellationToken);

    Task<AdmissionTicket?> GetIssuedTicketAsync(
        Guid tenantId,
        Guid registrationTicketAssignmentId,
        CancellationToken cancellationToken);

    Task<ParticipantAdmissionEligibility?> LoadForUpdateAsync(
        Guid tenantId,
        Guid registrationTicketAssignmentId,
        CancellationToken cancellationToken);

    Task<ParticipantAdmissionCompletionContext?>
        LoadCompletionForUpdateAsync(
            Guid tenantId,
            Guid eventId,
            Guid registrationOrderId,
            Guid registrationTicketAssignmentId,
            Guid participantId,
            Guid subjectUserId,
            CancellationToken cancellationToken);

    Task<AdmissionTicket?> GetIssuedTicketForUpdateAsync(
        Guid tenantId,
        Guid registrationTicketAssignmentId,
        CancellationToken cancellationToken);

    Task AddAsync(
        ParticipantAdmissionEligibility eligibility,
        CancellationToken cancellationToken);

    Task EnsureForAssignmentsAsync(
        Guid tenantId,
        Guid eventId,
        Guid registrationOrderId,
        IReadOnlyCollection<Guid> assignmentIds,
        DateTime createdAt,
        CancellationToken cancellationToken);

    Task ApplyDecisionAsync(
        ParticipantAdmissionEligibility eligibility,
        CancellationToken cancellationToken);
}
