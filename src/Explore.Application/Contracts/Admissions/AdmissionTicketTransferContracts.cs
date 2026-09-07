using Explore.Domain;

namespace Explore.Application.Contracts.Admissions;

public sealed record AdmissionTicketTransferOfferRequest(
    Guid TenantId,
    Guid EventId,
    Guid AdmissionTicketId,
    Guid OfferOperationKey,
    string CapabilityDigest,
    DateTime EventStartsAtUtc,
    DateTime OfferedAtUtc,
    Guid? AuthorityUserId = null);

public sealed record AdmissionTicketTransferAcceptanceRequest(
    Guid TenantId,
    Guid EventId,
    Guid AdmissionTicketId,
    Guid AdmissionTicketTransferId,
    string CapabilityDigest,
    int ExpectedCredentialGeneration,
    Guid RecipientParticipantId,
    Guid RecipientSubjectUserId,
    bool RequirementsComplete,
    Guid? SubjectConsentRecordId,
    Guid? ApprovedByActorId,
    Guid CredentialId,
    int LookupKeyVersion,
    string LookupDigest,
    Guid DeliveryIntentId,
    Guid OutboxMessageId,
    DateTime AcceptedAtUtc,
    Guid? AuthorityUserId = null);

public sealed record AdmissionTicketTransferContext(
    RegistrationTicketAssignment Assignment,
    ParticipantAdmissionEligibility Eligibility,
    AdmissionTicket Ticket,
    TicketTransferPolicy Policy,
    AdmissionTicketTransfer? Transfer,
    bool AlreadyCheckedIn);

public sealed record ParticipantAdmissionTransferReadiness(
    bool RequirementsComplete,
    Guid? SubjectConsentRecordId,
    Guid? ApprovedByActorId)
{
    public bool IsReady(
        ParticipantAdmissionEligibility eligibility) =>
        RequirementsComplete
        && (!eligibility.ConsentRequired
            || SubjectConsentRecordId.HasValue)
        && (!eligibility.ApprovalRequired
            || ApprovedByActorId.HasValue);
}

public sealed record AdmissionTicketTransferResult(
    AdmissionTicketTransferOutcome Outcome,
    AdmissionTicketTransfer? Transfer,
    AdmissionTicket? Ticket);

public sealed record AdmissionTicketTransferAccessContext(
    AdmissionTicketTransfer Transfer,
    AdmissionTicket Ticket,
    RegistrationOrder Order,
    RegistrationParticipant SourceParticipant,
    RegistrationParticipant? RecipientParticipant);

public interface IAdmissionTicketTransferRepository
{
    Task<AdmissionTicket?> GetTicketAsync(
        Guid tenantId,
        Guid eventId,
        Guid admissionTicketId,
        CancellationToken cancellationToken);

    Task<RegistrationOrder?> GetOrderAsync(
        Guid tenantId,
        Guid eventId,
        Guid registrationOrderId,
        CancellationToken cancellationToken);

    Task<DateTime?> GetEventStartsAtUtcAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken);

    Task<AdmissionTicketTransferAccessContext?> GetAccessAsync(
        Guid tenantId,
        Guid eventId,
        Guid admissionTicketId,
        Guid admissionTicketTransferId,
        CancellationToken cancellationToken);

    Task<AdmissionTicketTransferContext?> LoadForOfferAsync(
        Guid tenantId,
        Guid eventId,
        Guid admissionTicketId,
        CancellationToken cancellationToken);

    Task<AdmissionTicketTransferContext?> LoadForAcceptanceAsync(
        Guid tenantId,
        Guid eventId,
        Guid admissionTicketId,
        Guid admissionTicketTransferId,
        CancellationToken cancellationToken);

    Task<AdmissionTicketTransfer?> ResolveCapabilityForUpdateAsync(
        Guid tenantId,
        Guid eventId,
        Guid admissionTicketId,
        string capabilityDigest,
        CancellationToken cancellationToken);

    Task<AdmissionTicketTransferResult> OfferAsync(
        AdmissionTicketTransferOfferRequest request,
        CancellationToken cancellationToken);

    Task<AdmissionTicketTransferResult> ApplyAcceptanceAsync(
        AdmissionTicketTransferAcceptanceRequest request,
        CancellationToken cancellationToken);

    Task<AdmissionTicketTransferResult> CancelAsync(
        Guid tenantId,
        Guid eventId,
        Guid admissionTicketId,
        Guid admissionTicketTransferId,
        Guid authorityUserId,
        DateTime cancelledAtUtc,
        CancellationToken cancellationToken);

    Task<AdmissionTicketTransferResult> RotateForHolderAsync(
        Guid tenantId,
        Guid eventId,
        Guid admissionTicketId,
        Guid admissionTicketTransferId,
        Guid authorityUserId,
        Guid credentialId,
        int lookupKeyVersion,
        string lookupDigest,
        Guid outboxMessageId,
        string eventType,
        DateTime rotatedAtUtc,
        CancellationToken cancellationToken);

    Task<AdmissionTicketTransferContext?> LoadForCorrectionAsync(
        Guid tenantId,
        Guid eventId,
        Guid admissionTicketId,
        CancellationToken cancellationToken);

    Task<AdmissionTicketTransferContext?> LoadForReissueAsync(
        Guid tenantId,
        Guid eventId,
        Guid admissionTicketId,
        CancellationToken cancellationToken);
}
