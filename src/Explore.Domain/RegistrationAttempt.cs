using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed class RegistrationAttempt : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    private RegistrationAttempt()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; private set; }
    public Guid RegistrationOrderId { get; private set; }
    public Guid RegistrationWorkflowId { get; private set; }
    public Guid RegistrationRequirementId { get; private set; }
    public Guid RegistrationChannelId { get; private set; }
    public Guid RegistrationFormId { get; private set; }
    public Guid RegistrationFormVersionId { get; private set; }
    public Guid? RegistrationProviderBindingId { get; private set; }
    public RegistrationEvidenceHash? ProviderMappingRevisionHash { get; private set; }
    public CapabilityTokenHash CapabilityTokenHash { get; private set; } = null!;
    public DateTime ExpiresAt { get; private set; }
    public DateTime? ConsumedAt { get; private set; }
    public Guid? SubmissionConsumptionClaimId { get; private set; }
    public DateTime? SupersededAt { get; private set; }
    public Guid? SupersededByRegistrationAttemptId { get; private set; }
    public string? SupersessionReason { get; private set; }
    public int StatusId { get; private set; }
    public RegistrationAttemptStatus? Status { get; private set; }
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public static RegistrationAttempt Create(
        Guid tenantId,
        Guid eventId,
        Guid registrationOrderId,
        Guid registrationWorkflowId,
        Guid registrationRequirementId,
        Guid registrationChannelId,
        Guid registrationFormId,
        Guid registrationFormVersionId,
        CapabilityTokenHash capabilityTokenHash,
        Guid? registrationProviderBindingId,
        RegistrationEvidenceHash? providerMappingRevisionHash,
        DateTime createdAt,
        DateTime expiresAt) => Create(
        Guid.CreateVersion7(),
        tenantId,
        eventId,
        registrationOrderId,
        registrationWorkflowId,
        registrationRequirementId,
        registrationChannelId,
        registrationFormId,
        registrationFormVersionId,
        capabilityTokenHash,
        registrationProviderBindingId,
        providerMappingRevisionHash,
        createdAt,
        expiresAt);

    public static RegistrationAttempt Create(
        Guid id,
        Guid tenantId,
        Guid eventId,
        Guid registrationOrderId,
        Guid registrationWorkflowId,
        Guid registrationRequirementId,
        Guid registrationChannelId,
        Guid registrationFormId,
        Guid registrationFormVersionId,
        CapabilityTokenHash capabilityTokenHash,
        Guid? registrationProviderBindingId,
        RegistrationEvidenceHash? providerMappingRevisionHash,
        DateTime createdAt,
        DateTime expiresAt)
    {
        ArgumentNullException.ThrowIfNull(capabilityTokenHash);
        if (new[] { id, tenantId, eventId, registrationOrderId, registrationWorkflowId, registrationRequirementId, registrationChannelId, registrationFormId, registrationFormVersionId }.Any(value => value == Guid.Empty))
        {
            throw new ArgumentException("Attempt lineage identities are required.");
        }

        if ((registrationProviderBindingId is null) != (providerMappingRevisionHash is null) || registrationProviderBindingId == Guid.Empty)
        {
            throw new ArgumentException("Provider binding and mapping revision evidence must be supplied together.");
        }

        DateTime normalizedCreatedAt = EnsureUtc(createdAt, nameof(createdAt));
        DateTime normalizedExpiresAt = EnsureUtc(expiresAt, nameof(expiresAt));
        if (normalizedExpiresAt <= normalizedCreatedAt)
        {
            throw new ArgumentException("Attempt expiry must be after creation.", nameof(expiresAt));
        }

        return new RegistrationAttempt
        {
            Id = id,
            TenantId = tenantId,
            EventId = eventId,
            RegistrationOrderId = registrationOrderId,
            RegistrationWorkflowId = registrationWorkflowId,
            RegistrationRequirementId = registrationRequirementId,
            RegistrationChannelId = registrationChannelId,
            RegistrationFormId = registrationFormId,
            RegistrationFormVersionId = registrationFormVersionId,
            RegistrationProviderBindingId = registrationProviderBindingId,
            ProviderMappingRevisionHash = providerMappingRevisionHash,
            CapabilityTokenHash = capabilityTokenHash,
            CreatedAt = normalizedCreatedAt,
            ExpiresAt = normalizedExpiresAt,
            StatusId = (int)RegistrationAttemptStatusEnum.Active
        };
    }

    public bool IsExpiredAt(DateTime timestamp) => EnsureUtc(timestamp, nameof(timestamp)) >= ExpiresAt;

    public bool CanAcceptSubmissionAt(DateTime timestamp) =>
        StatusId == (int)RegistrationAttemptStatusEnum.Active && !IsExpiredAt(timestamp);

    public RegistrationSubmission SubmitNative(
        RegistrationEvidenceHash receivedEvidenceHash,
        DateTime receivedAt,
        RegistrationTransportIdempotencyHash? httpIdempotencyKeyHash)
    {
        DateTime timestamp = EnsureUtc(receivedAt, nameof(receivedAt));
        EnsureNativePinned();
        EnsureCanSubmitAt(timestamp);
        RegistrationSubmission.ValidateAcceptedNative(this, receivedEvidenceHash, timestamp);
        Guid claimId = Guid.CreateVersion7();
        RegistrationSubmission submission = RegistrationSubmission.CreateAcceptedNative(this, receivedEvidenceHash, timestamp, httpIdempotencyKeyHash, claimId);
        ConsumeForSubmission(timestamp, claimId);
        return submission;
    }

    public RegistrationSubmission SubmitHeadlessProvider(
        RegistrationEvidenceHash receivedEvidenceHash,
        DateTime receivedAt,
        RegistrationTransportIdempotencyHash? httpIdempotencyKeyHash)
    {
        DateTime timestamp = EnsureUtc(receivedAt, nameof(receivedAt));
        EnsureProviderPinned();
        EnsureCanSubmitAt(timestamp);
        Guid claimId = Guid.CreateVersion7();
        RegistrationSubmission submission = RegistrationSubmission.CreateAcceptedHeadlessProvider(
            this, receivedEvidenceHash, timestamp, httpIdempotencyKeyHash, claimId);
        ConsumeForSubmission(timestamp, claimId);
        return submission;
    }

    public RegistrationSubmission SubmitProvider(
        RegistrationEvidenceHash receivedEvidenceHash,
        DateTime receivedAt,
        RegistrationTransportIdempotencyHash? httpIdempotencyKeyHash,
        string providerSubmissionId,
        string providerResponseRevision,
        string? providerSubjectId,
        string? providerCorrelationId)
    {
        DateTime timestamp = EnsureUtc(receivedAt, nameof(receivedAt));
        EnsureProviderPinned();
        EnsureCanSubmitAt(timestamp);
        RegistrationSubmission.ValidateAcceptedProvider(this, receivedEvidenceHash, timestamp, providerSubmissionId, providerResponseRevision);
        Guid claimId = Guid.CreateVersion7();
        RegistrationSubmission submission = RegistrationSubmission.CreateAcceptedProvider(
            this,
            receivedEvidenceHash,
            timestamp,
            httpIdempotencyKeyHash,
            providerSubmissionId,
            providerResponseRevision,
            providerSubjectId,
            providerCorrelationId,
            claimId);
        ConsumeForSubmission(timestamp, claimId);
        return submission;
    }

    public void Consume(DateTime consumedAt)
    {
        DateTime timestamp = EnsureUtc(consumedAt, nameof(consumedAt));
        EnsureNotBeforeCreated(timestamp, nameof(consumedAt));
        EnsureActive();
        if (timestamp >= ExpiresAt)
        {
            throw new InvalidOperationException("Expired registration attempts cannot be consumed.");
        }

        StatusId = (int)RegistrationAttemptStatusEnum.Consumed;
        ConsumedAt = timestamp;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void Expire(DateTime expiredAt)
    {
        DateTime timestamp = EnsureUtc(expiredAt, nameof(expiredAt));
        EnsureNotBeforeCreated(timestamp, nameof(expiredAt));
        EnsureActive();
        if (timestamp < ExpiresAt)
        {
            throw new InvalidOperationException("Registration attempts cannot expire before their pinned expiry instant.");
        }

        StatusId = (int)RegistrationAttemptStatusEnum.Expired;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void Supersede(Guid replacementAttemptId, DateTime supersededAt, string reason)
    {
        if (replacementAttemptId == Guid.Empty || replacementAttemptId == Id)
        {
            throw new ArgumentException("Replacement attempt identity is required.", nameof(replacementAttemptId));
        }

        DateTime timestamp = EnsureUtc(supersededAt, nameof(supersededAt));
        EnsureNotBeforeCreated(timestamp, nameof(supersededAt));
        EnsureActive();
        if (timestamp >= ExpiresAt)
        {
            throw new InvalidOperationException("Expired registration attempts cannot be superseded.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        StatusId = (int)RegistrationAttemptStatusEnum.Superseded;
        SupersededAt = timestamp;
        SupersededByRegistrationAttemptId = replacementAttemptId;
        SupersessionReason = reason.Trim();
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    private void ConsumeForSubmission(DateTime timestamp, Guid claimId)
    {
        if (claimId == Guid.Empty)
        {
            throw new ArgumentException("Submission consumption claim is required.", nameof(claimId));
        }

        StatusId = (int)RegistrationAttemptStatusEnum.Consumed;
        ConsumedAt = timestamp;
        SubmissionConsumptionClaimId = claimId;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    private void EnsureCanSubmitAt(DateTime timestamp)
    {
        EnsureNotBeforeCreated(timestamp, nameof(timestamp));
        EnsureActive();
        if (timestamp >= ExpiresAt)
        {
            throw new InvalidOperationException("Expired registration attempts cannot be consumed.");
        }
    }

    private void EnsureNativePinned()
    {
        if (RegistrationProviderBindingId is not null || ProviderMappingRevisionHash is not null)
        {
            throw new InvalidOperationException("Provider-pinned registration attempts cannot accept native submissions.");
        }
    }

    private void EnsureProviderPinned()
    {
        if (RegistrationProviderBindingId is null || ProviderMappingRevisionHash is null)
        {
            throw new ArgumentException("Provider submissions require pinned provider binding and mapping revision evidence.");
        }
    }

    private void EnsureNotBeforeCreated(DateTime timestamp, string parameterName)
    {
        if (timestamp < CreatedAt)
        {
            throw new ArgumentException("Runtime transition cannot predate attempt creation.", parameterName);
        }
    }

    private void EnsureActive()
    {
        if (StatusId != (int)RegistrationAttemptStatusEnum.Active)
        {
            throw new InvalidOperationException("Only active registration attempts can transition.");
        }
    }

    private static DateTime EnsureUtc(DateTime value, string parameterName)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be a non-default UTC value.", parameterName);
        }

        return value;
    }
}
