using Explore.Domain;

namespace Explore.Application.Contracts.Admissions;

public enum AdmissionIssuanceOutcome
{
    Issued,
    AlreadyIssued,
    NotConfirmed,
    NoAssignments,
    ReadinessPending,
    InvalidRequest,
    CancelledBeforeCommit
}

public enum AdmissionDeliveryOutcome
{
    NotRequired,
    Delivered,
    RecoverablePending,
    Unrecoverable
}

public enum AdmissionDeliveryFailure
{
    None,
    Cancelled,
    RouteUnavailable,
    EnvelopeUnavailable,
    InvalidIntent,
    RetentionExpired
}

public enum AdmissionCredentialVerificationOutcome
{
    Match,
    Mismatch,
    KeyUnavailable,
    MalformedDigest,
    InvalidRequest
}

public static class AdmissionIssuanceAuthority
{
    public const string ConfirmedFreeOrder = "ConfirmedFreeOrder";
    public const string ReconciledPaidFinalization = "ReconciledPaidFinalization";

    public static string ForOrderTotal(long totalDueMinor) => totalDueMinor switch
    {
        < 0 => throw new ArgumentOutOfRangeException(nameof(totalDueMinor)),
        0 => ConfirmedFreeOrder,
        _ => ReconciledPaidFinalization
    };
}

public sealed record AdmissionIssuanceRequest(
    Guid TenantId,
    Guid RegistrationOrderId,
    Guid FinalizationEffectId,
    string Authority);

public interface IAdmissionIssuanceService
{
    Task<AdmissionIssuanceResult> IssueConfirmedAsync(
        AdmissionIssuanceRequest request,
        CancellationToken cancellationToken);
}

public sealed record AdmissionAssignmentFact(
    RegistrationOrderLine OrderLine,
    RegistrationTicketAssignment Assignment,
    RegistrationParticipant Participant,
    EventTicketType EventTicketType,
    long LineUnitMinor,
    long RelevantLineTotalMinor,
    bool IsAdmissionLine,
    ParticipantAdmissionReadinessDecision? Readiness = null)
{
    public Guid RegistrationTicketAssignmentId => Assignment.Id;
    public Guid RegistrationOrderLineId => OrderLine.Id;
    public Guid RegistrationParticipantId => Participant.Id;
}

public sealed record AdmissionIssuanceContext(
    Guid TenantId,
    Guid EventId,
    Guid RegistrationOrderId,
    Guid FinalizationEffectId,
    string Authority,
    bool PaymentReconciled,
    bool OrderConfirmed,
    RegistrationOrder Order,
    EventTicketCatalogVersion TicketCatalogVersion,
    IReadOnlyList<AdmissionAssignmentFact> Assignments,
    IReadOnlyList<AdmissionTicket> ExistingTickets,
    string DeliveryAddress,
    IReadOnlyList<AdmissionDeliveryIntent>? ExistingDeliveryIntents = null)
{
    public DateTime? DeliveryDisclosureUntilUtc { get; init; }
    public Guid? DeliveryAccountUserId { get; init; }
}

public sealed record AdmissionCredentialCreateRequest(
    Guid TenantId,
    Guid AdmissionTicketId,
    Guid AdmissionCredentialId,
    string Purpose,
    int CredentialVersion);

public sealed record AdmissionCredentialVerificationRequest(
    Guid TenantId,
    int PersistedKeyVersion,
    string Purpose,
    string PlaintextCredential,
    string ExpectedDigest)
{
    public override string ToString() =>
        $"AdmissionCredentialVerificationRequest(tenant={TenantId}, keyVersion={PersistedKeyVersion}, purpose={Purpose}, <redacted>)";
}

public sealed class AdmissionCredentialMaterial
{
    public AdmissionCredentialMaterial(
        string plaintextCredential,
        string lookupDigest,
        int keyVersion,
        int credentialVersion)
    {
        PlaintextCredential = plaintextCredential;
        LookupDigest = lookupDigest;
        KeyVersion = keyVersion;
        CredentialVersion = credentialVersion;
    }

    public string PlaintextCredential { get; }
    public string LookupDigest { get; }
    public int KeyVersion { get; }
    public int CredentialVersion { get; }

    public override string ToString() =>
        $"AdmissionCredentialMaterial(keyVersion={KeyVersion}, credentialVersion={CredentialVersion}, <redacted>)";
}

public sealed class AdmissionOneTimeCredential
{
    public AdmissionOneTimeCredential(Guid admissionTicketId, string plaintextCredential)
    {
        AdmissionTicketId = admissionTicketId;
        PlaintextCredential = plaintextCredential;
    }

    public Guid AdmissionTicketId { get; }
    public string PlaintextCredential { get; }

    public override string ToString() => $"AdmissionOneTimeCredential(ticket={AdmissionTicketId}, <redacted>)";
}

public sealed record AdmissionCredentialDeliveryEnvelope(
    string RecipientAddress,
    string PlaintextCredential)
{
    public DateTime? DisclosureUntilUtc { get; init; }
    public Guid? AccountUserId { get; init; }

    public override string ToString() => "AdmissionCredentialDeliveryEnvelope(<redacted>)";
}

public sealed record AdmissionProtectedDeliveryMaterial(string Ciphertext, int ProtectionVersion)
{
    public override string ToString() =>
        $"AdmissionProtectedDeliveryMaterial(protectionVersion={ProtectionVersion}, <redacted>)";
}

public sealed class AdmissionDeliveryIntent
{
    private AdmissionDeliveryIntent()
    {
    }

    public AdmissionDeliveryIntent(
        Guid id,
        Guid tenantId,
        Guid finalizationEffectId,
        Guid registrationTicketAssignmentId,
        Guid admissionTicketId,
        string protectedCredential,
        int protectionVersion,
        DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(protectedCredential) || protectionVersion < 1)
        {
            throw new ArgumentException("Protected admission delivery material is required.");
        }

        Id = id;
        TenantId = tenantId;
        FinalizationEffectId = finalizationEffectId;
        RegistrationTicketAssignmentId = registrationTicketAssignmentId;
        AdmissionTicketId = admissionTicketId;
        ProtectedCredential = protectedCredential;
        ProtectionVersion = protectionVersion;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid FinalizationEffectId { get; private set; }
    public Guid RegistrationTicketAssignmentId { get; private set; }
    public Guid AdmissionTicketId { get; private set; }
    public string ProtectedCredential { get; private set; } = string.Empty;
    public int ProtectionVersion { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? RoutedAt { get; private set; }
    public DateTime? HandoffCompletedAt { get; private set; }
    public string? HandoffReceiptId { get; private set; }
    public Guid ConcurrencyStamp { get; set; }

    public void MarkRouted(DateTime routedAtUtc)
    {
        if (routedAtUtc.Kind != DateTimeKind.Utc || routedAtUtc < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(routedAtUtc));
        }

        RoutedAt ??= routedAtUtc;
    }

    public void CompleteHandoff(string receiptId, DateTime completedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(receiptId))
        {
            throw new ArgumentException("A channel receipt is required.", nameof(receiptId));
        }
        if (completedAtUtc.Kind != DateTimeKind.Utc || completedAtUtc < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(completedAtUtc));
        }
        if (RoutedAt is null)
        {
            throw new InvalidOperationException("Admission delivery must be routed before handoff completion.");
        }

        HandoffCompletedAt ??= completedAtUtc;
        HandoffReceiptId ??= receiptId.Trim();
        ProtectedCredential = string.Empty;
    }

    public override string ToString() =>
        $"AdmissionDeliveryIntent({Id}, ticket={AdmissionTicketId}, routed={RoutedAt is not null}, handoff={HandoffCompletedAt is not null}, <redacted>)";
}

public sealed record AdmissionIssuancePersistenceRequest(
    Guid TenantId,
    Guid RegistrationOrderId,
    Guid FinalizationEffectId,
    IReadOnlyList<AdmissionTicket> Tickets,
    IReadOnlyList<AdmissionDeliveryIntent> DeliveryIntents);

public sealed class AdmissionIssuanceResult
{
    public AdmissionIssuanceResult(
        AdmissionIssuanceOutcome outcome,
        IReadOnlyList<Guid> issuedTicketIds,
        IReadOnlyList<Guid> existingTicketIds,
        IReadOnlyList<AdmissionTicket> tickets,
        IReadOnlyList<Guid> deliveryIntentIds,
        IReadOnlyList<AdmissionOneTimeCredential>? oneTimeCredentials = null,
        AdmissionDeliveryOutcome deliveryOutcome = AdmissionDeliveryOutcome.NotRequired,
        AdmissionDeliveryFailure deliveryFailure = AdmissionDeliveryFailure.None)
    {
        Outcome = outcome;
        IssuedTicketIds = issuedTicketIds;
        ExistingTicketIds = existingTicketIds;
        Tickets = tickets;
        DeliveryIntentIds = deliveryIntentIds;
        OneTimeCredentials = oneTimeCredentials ?? [];
        DeliveryOutcome = deliveryOutcome;
        DeliveryFailure = deliveryFailure;
    }

    public AdmissionIssuanceOutcome Outcome { get; }
    public IReadOnlyList<Guid> IssuedTicketIds { get; }
    public IReadOnlyList<Guid> ExistingTicketIds { get; }
    public IReadOnlyList<AdmissionTicket> Tickets { get; }
    public IReadOnlyList<Guid> DeliveryIntentIds { get; }
    public IReadOnlyList<AdmissionOneTimeCredential> OneTimeCredentials { get; }
    public AdmissionDeliveryOutcome DeliveryOutcome { get; }
    public AdmissionDeliveryFailure DeliveryFailure { get; }

    public override string ToString() =>
        $"AdmissionIssuanceResult(outcome={Outcome}, delivery={DeliveryOutcome}, issued={IssuedTicketIds.Count}, existing={ExistingTicketIds.Count}, <redacted>)";
}

public static class AdmissionDeliveryEvents
{
    public const string CredentialDeliveryRequested = "AdmissionCredentialDeliveryRequested";
}

public sealed record AdmissionDeliveryDispatchRequest(Guid DeliveryIntentId);
public sealed record AdmissionDeliveryDispatchResult(
    AdmissionDeliveryOutcome Outcome,
    AdmissionDeliveryFailure Failure = AdmissionDeliveryFailure.None);

public sealed record AdmissionCredentialDirectDeliveryRequest(
    Guid TenantId,
    Guid DeliveryIntentId,
    Guid AdmissionTicketId,
    string RecipientAddress,
    string PlaintextCredential)
{
    public DateTime? DisclosureUntilUtc { get; init; }

    public override string ToString() =>
        $"AdmissionCredentialDirectDeliveryRequest(tenant={TenantId}, intent={DeliveryIntentId}, ticket={AdmissionTicketId}, <redacted>)";
}

public enum AdmissionCredentialDirectDeliveryOutcome
{
    Accepted,
    Ambiguous,
    RetentionExpired
}

public sealed record AdmissionCredentialDirectDeliveryResult(
    AdmissionCredentialDirectDeliveryOutcome Outcome,
    string? ReceiptId = null);

public interface IAdmissionIssuanceRepository
{
    Task<AdmissionIssuanceContext?> LoadAsync(
        AdmissionIssuanceRequest request,
        CancellationToken cancellationToken);

    Task<AdmissionIssuanceContext?> ReloadCommittedAsync(
        AdmissionIssuanceRequest request,
        CancellationToken cancellationToken);

    Task<AdmissionIssuanceResult> IssueAndScheduleDeliveryAsync(
        AdmissionIssuancePersistenceRequest request,
        CancellationToken cancellationToken);
}

public interface IAdmissionCredentialDigestService
{
    Task<AdmissionCredentialMaterial> CreateAsync(
        AdmissionCredentialCreateRequest request,
        CancellationToken cancellationToken);

    Task<AdmissionCredentialVerificationOutcome> VerifyAsync(
        AdmissionCredentialVerificationRequest request,
        CancellationToken cancellationToken);
}

public interface IAdmissionDeliveryEnvelopeProtector
{
    AdmissionProtectedDeliveryMaterial Protect(AdmissionCredentialDeliveryEnvelope envelope);
    AdmissionCredentialDeliveryEnvelope Unprotect(string ciphertext, int protectionVersion);
}

public interface IAdmissionDeliveryDispatcher
{
    Task<AdmissionDeliveryDispatchResult> DispatchAsync(
        AdmissionDeliveryDispatchRequest request,
        CancellationToken cancellationToken);
}

public interface IAdmissionCredentialDirectDeliveryChannel
{
    Task<AdmissionCredentialDirectDeliveryResult> DeliverAsync(
        AdmissionCredentialDirectDeliveryRequest request,
        CancellationToken cancellationToken);
}

public interface IAdmissionCredentialDeliveryOutboxHandler
{
    Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken);
}
