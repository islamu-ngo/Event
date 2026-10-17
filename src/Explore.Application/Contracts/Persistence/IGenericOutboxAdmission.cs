using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public enum GenericOutboxOwnership
{
    Tenant = 1,
    Subject = 2,
    Instance = 3
}

/// <summary>Identity-only admission; it carries neither source content nor provider credentials.</summary>
public sealed record GenericOutboxClaim(
    Guid MessageId,
    Guid? TenantId,
    GenericOutboxOwnership Ownership,
    Guid SourceId,
    string SourceIdentity,
    Guid AggregateId,
    string AggregateType,
    string EventType,
    string PayloadDigest,
    DateTime CreatedAt,
    OutboxMessageStatus Status,
    DateTime LeaseExpiresAt);

public interface IGenericOutboxAdmission
{
    Task<IReadOnlyList<GenericOutboxClaim>> ClaimDueAsync(
        int batchSize, DateTime claimedAt, CancellationToken cancellationToken);

    Task<GenericOutboxClaim?> ReadClaimAsync(
        Guid messageId, DateTime leaseExpiresAt, OutboxMessageStatus status, CancellationToken cancellationToken);
}

/// <summary>The complete generic route set; notification admission is a separate capability.</summary>
public static class GenericOutboxRoutes
{
    public static bool Contains(string eventType) => eventType is
        "EventReportProviderSyncRequested" or
        "LocationPiiErased" or "LocationPrivacyCorrectionRequested" or "location.privacy.corrected" or
        "PrivacyErasureCacheInvalidationRequested" or
        "ConfigurationManifestEffectsRequested" or "ConfigurationImportEffectsRequested" or
        "OperatorIdentityImported" or
        "RegistrationOrderRejected" or "RegistrationOrderConfirmed" or "RegistrationOrderCancelled" or
        "admission.event_cancellation.requested" or
        "AdmissionCredentialDeliveryRequested" or "AdmissionRecoveryRequestProcessingRequested" or
        "AdmissionRecoveryDeliveryRequested" or
        "registration.refund_campaign.process_requested" or "registration.refund.dispatch_requested" or
        "registration.refund.reconciliation_requested" or "registration.payment.cancellation_requested" or
        "ManagedTenantProvisioningProcessRequested";

    public static bool SupportsDeadLetter(string eventType) => Contains(eventType)
        && eventType is not ("EventReportProviderSyncRequested" or
            "RegistrationOrderRejected" or "RegistrationOrderConfirmed");
}
