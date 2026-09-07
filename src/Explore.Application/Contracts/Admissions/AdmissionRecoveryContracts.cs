namespace Explore.Application.Contracts.Admissions;

public enum AdmissionRecoveryPurpose
{
    TicketRecovery,
    TransferAcceptance
}

public enum AdmissionRecoveryRequestOutcome
{
    Accepted
}

public enum AdmissionRecoveryConsumeOutcome
{
    Consumed,
    AlreadyConsumed,
    Expired,
    WrongPurpose,
    WrongTenant,
    Rotated,
    Invalid
}

public enum AdmissionRecoveryDeliveryOutcome
{
    Accepted,
    Pending
}

public sealed record AdmissionRecoveryRequest(
    Guid TenantId,
    string NormalizedIdentity,
    AdmissionRecoveryPurpose Purpose);

public sealed record AdmissionRecoveryConsumeRequest(
    Guid TenantId,
    Guid RecoveryRequestId,
    string Capability,
    AdmissionRecoveryPurpose Purpose)
{
    public override string ToString() => "AdmissionRecoveryConsumeRequest(<redacted>)";
}

public sealed record AdmissionRecoveryResendRequest(
    Guid TenantId,
    Guid RecoveryRequestId,
    AdmissionRecoveryPurpose Purpose);

public sealed record AdmissionRecoveryRequestResult(AdmissionRecoveryRequestOutcome Outcome);

public sealed record AdmissionRecoveryConsumeResult(
    AdmissionRecoveryConsumeOutcome Outcome,
    Guid RecoveryRecordId = default,
    AdmissionRecoveryTicketDocument? Document = null);

public sealed record AdmissionRecoveryResendResult(AdmissionRecoveryRequestOutcome Outcome);

public sealed record AdmissionRecoveryIdentityResult
{
    public AdmissionRecoveryIdentityResult(
        Guid tenantId,
        Guid recoveryRequestId,
        bool identityPresent,
        IReadOnlyList<Guid> admissionTicketIds)
    {
        TenantId = tenantId;
        RecoveryRequestId = recoveryRequestId;
        IdentityPresent = identityPresent;
        AdmissionTicketIds = admissionTicketIds.ToArray();
    }

    public Guid TenantId { get; }
    public Guid RecoveryRequestId { get; }
    public bool IdentityPresent { get; }
    public IReadOnlyList<Guid> AdmissionTicketIds { get; }
}

public sealed record AdmissionRecoveryCapabilityIssueRequest(
    Guid TenantId,
    Guid RecoveryRequestId,
    Guid AdmissionTicketId,
    AdmissionRecoveryPurpose Purpose,
    int KeyVersion = 0);

public sealed record AdmissionRecoveryCapabilityDigestRequest(
    Guid TenantId,
    Guid RecoveryRequestId,
    Guid AdmissionTicketId,
    AdmissionRecoveryPurpose Purpose,
    string Capability,
    int KeyVersion = 0)
{
    public override string ToString() => "AdmissionRecoveryCapabilityDigestRequest(<redacted>)";
}

public sealed record AdmissionRecoveryCapabilityMaterial
{
    public AdmissionRecoveryCapabilityMaterial(
        string capability,
        string lookupDigest,
        int keyVersion,
        AdmissionRecoveryPurpose purpose,
        DateTimeOffset expiresAtUtc,
        string locatorDigest = "")
    {
        Capability = capability;
        LookupDigest = lookupDigest;
        KeyVersion = keyVersion;
        Purpose = purpose;
        ExpiresAtUtc = expiresAtUtc;
        LocatorDigest = locatorDigest;
    }

    public string Capability { get; }
    public string LookupDigest { get; }
    public int KeyVersion { get; }
    public AdmissionRecoveryPurpose Purpose { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public string LocatorDigest { get; }

    public override string ToString() =>
        $"AdmissionRecoveryCapabilityMaterial(keyVersion={KeyVersion}, <redacted>)";
}

public sealed record AdmissionRecoveryCapabilityDigest(string LookupDigest, int KeyVersion)
{
    public override string ToString() =>
        $"AdmissionRecoveryCapabilityDigest(keyVersion={KeyVersion}, <redacted>)";
}

public sealed record AdmissionRecoveryLocatorDigest(string LocatorDigest, int KeyVersion)
{
    public override string ToString() =>
        $"AdmissionRecoveryLocatorDigest(keyVersion={KeyVersion}, <redacted>)";
}

public sealed record AdmissionRecoveryDeliveryRequest(
    Guid TenantId,
    Guid RecoveryRequestId,
    Guid AdmissionTicketId,
    AdmissionRecoveryPurpose Purpose,
    string Capability,
    int CapabilityVersion = 1)
{
    public override string ToString() => "AdmissionRecoveryDeliveryRequest(<redacted>)";
}

public sealed record AdmissionRecoveryDeliveryResult(AdmissionRecoveryDeliveryOutcome Outcome);

public sealed record AdmissionRecoveryTicketDocument(
    Guid TicketId,
    Guid EventId,
    string StatusCode,
    string DisplayReference,
    string ManualCode,
    string ManualCodeClassificationCode,
    string QrRepresentation,
    string PrintModel)
{
    public override string ToString() =>
        $"AdmissionRecoveryTicketDocument(ticket={TicketId}, status={StatusCode}, <redacted>)";
}

public sealed record AdmissionRecoveryAuditFact(
    Guid TenantId,
    Guid RecoveryRequestId,
    string ActionCode,
    int CapabilityVersion,
    DateTimeOffset OccurredAtUtc);

public sealed record AdmissionRecoveryRateLimitDecision(
    bool Allowed,
    int RetryAfterSeconds = 0);

public sealed record AdmissionRecoveryRequestEnvelope(
    string NormalizedIdentity,
    AdmissionRecoveryPurpose Purpose)
{
    public override string ToString() => "AdmissionRecoveryRequestEnvelope(<redacted>)";
}

public sealed record AdmissionRecoveryDeliveryEnvelope(
    string RecipientAddress,
    Guid RecoveryRequestId,
    string Capability)
{
    public override string ToString() => "AdmissionRecoveryDeliveryEnvelope(<redacted>)";
}

public sealed record AdmissionRecoveryProtectedDeliveryMaterial(string Ciphertext, int ProtectionVersion)
{
    public override string ToString() =>
        $"AdmissionRecoveryProtectedDeliveryMaterial(protectionVersion={ProtectionVersion}, <redacted>)";
}

public static class AdmissionRecoveryDeliveryEvents
{
    public const string RecoveryRequestProcessingRequested =
        "AdmissionRecoveryRequestProcessingRequested";
    public const string RecoveryDeliveryRequested = "AdmissionRecoveryDeliveryRequested";
}

public sealed record AdmissionRecoveryRequestPointer(Guid TenantId, Guid RequestIntentId);

public sealed record AdmissionRecoveryDeliveryPointer(
    Guid TenantId,
    Guid AdmissionTicketId,
    Guid DeliveryIntentId);

public enum AdmissionRecoveryDirectDeliveryOutcome
{
    Accepted,
    Ambiguous
}

public sealed record AdmissionRecoveryDirectDeliveryRequest(
    Guid TenantId,
    Guid DeliveryIntentId,
    Guid AdmissionTicketId,
    Guid RecoveryRequestId,
    string RecipientAddress,
    string Capability)
{
    public override string ToString() => "AdmissionRecoveryDirectDeliveryRequest(<redacted>)";
}

public sealed record AdmissionRecoveryDirectDeliveryResult(
    AdmissionRecoveryDirectDeliveryOutcome Outcome,
    string? ReceiptId = null);

public interface IAdmissionRecoveryCapabilityService
{
    Task<AdmissionRecoveryCapabilityMaterial> IssueAsync(
        AdmissionRecoveryCapabilityIssueRequest request,
        CancellationToken cancellationToken);

    Task<AdmissionRecoveryCapabilityDigest> DigestAsync(
        AdmissionRecoveryCapabilityDigestRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AdmissionRecoveryLocatorDigest>> DigestLocatorsAsync(
        string capability,
        CancellationToken cancellationToken);
}

public interface IAdmissionRecoveryDeliveryStager
{
    Task<AdmissionRecoveryDeliveryResult> StageAsync(
        AdmissionRecoveryDeliveryRequest request,
        CancellationToken cancellationToken);
}

public interface IAdmissionRecoveryRequestStager
{
    Task StageAsync(
        Guid tenantId,
        AdmissionRecoveryRequestEnvelope envelope,
        CancellationToken cancellationToken);
}

public interface IAdmissionRecoveryRequestEnvelopeProtector
{
    AdmissionRecoveryProtectedDeliveryMaterial Protect(AdmissionRecoveryRequestEnvelope envelope);
    AdmissionRecoveryRequestEnvelope Unprotect(string ciphertext, int protectionVersion);
}

public interface IAdmissionRecoveryRequestOutboxHandler
{
    Task HandleAsync(Explore.Domain.OutboxMessage message, CancellationToken cancellationToken);
}

public interface IAdmissionRecoveryDeliveryEnvelopeProtector
{
    AdmissionRecoveryProtectedDeliveryMaterial Protect(AdmissionRecoveryDeliveryEnvelope envelope);
    AdmissionRecoveryDeliveryEnvelope Unprotect(string ciphertext, int protectionVersion);
}

public interface IAdmissionRecoveryDirectDeliveryChannel
{
    Task<AdmissionRecoveryDirectDeliveryResult> DeliverAsync(
        AdmissionRecoveryDirectDeliveryRequest request,
        CancellationToken cancellationToken);
}

public interface IAdmissionRecoveryDeliveryOutboxHandler
{
    Task HandleAsync(Explore.Domain.OutboxMessage message, CancellationToken cancellationToken);
}

public interface IAdmissionRecoveryTicketDocumentService
{
    Task<AdmissionRecoveryTicketDocument?> RotateAndCreateAsync(
        Guid tenantId,
        Guid admissionTicketId,
        CancellationToken cancellationToken);
}

public interface IAdmissionRecoveryAuditService
{
    Task AppendAsync(
        AdmissionRecoveryAuditFact fact,
        CancellationToken cancellationToken);
}

public interface IAdmissionRecoveryRateLimiter
{
    AdmissionRecoveryRateLimitDecision TryAcquire(
        Guid tenantId,
        string normalizedIdentity,
        DateTimeOffset occurredAtUtc);
}

public interface IAdmissionTicketRecoveryRepository
{
    Task<Explore.Domain.AdmissionTicket?> GetForUpdateAsync(
        Guid tenantId,
        Guid admissionTicketId,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IAdmissionRecoveryIdentityResolver
{
    Task<AdmissionRecoveryIdentityResult> FindAsync(
        AdmissionRecoveryRequest request,
        CancellationToken cancellationToken);
}

public interface IAdmissionRecoveryRepository
{
    Task<Explore.Domain.AdmissionRecoveryCapability> AddAsync(
        Explore.Domain.AdmissionRecoveryCapability capability,
        CancellationToken cancellationToken);

    Task<Explore.Domain.AdmissionRecoveryCapability?> FindByProofDigestAsync(
        Guid tenantId,
        Guid recoveryRequestId,
        Guid admissionTicketId,
        AdmissionRecoveryPurpose purpose,
        int keyVersion,
        string lookupDigest,
        CancellationToken cancellationToken);

    Task<Explore.Domain.AdmissionRecoveryCapability?> FindByLocatorAsync(
        Guid tenantId,
        IReadOnlyList<AdmissionRecoveryLocatorDigest> locators,
        CancellationToken cancellationToken);

    Task<Explore.Domain.AdmissionRecoveryCapability?> FindLatestByRequestIdAsync(
        Guid tenantId,
        Guid recoveryRequestId,
        AdmissionRecoveryPurpose purpose,
        CancellationToken cancellationToken);

    Task<Explore.Domain.AdmissionRecoveryCapability?> FindLatestByTicketIdAsync(
        Guid tenantId,
        Guid admissionTicketId,
        AdmissionRecoveryPurpose purpose,
        CancellationToken cancellationToken);

    Task<bool> TryConsumeAsync(
        Guid tenantId,
        Guid capabilityId,
        int keyVersion,
        string lookupDigest,
        Guid expectedConcurrencyStamp,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken);

    Task<bool> TryRotateAsync(
        Explore.Domain.AdmissionRecoveryCapability current,
        Explore.Domain.AdmissionRecoveryCapability replacement,
        DateTime rotatedAtUtc,
        CancellationToken cancellationToken);
}
