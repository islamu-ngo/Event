using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class WebhookProviderPublication : ITenantEntity, IAuditableEntity
{
    public const int MaxIdentityLength = 256;
    public const int MaxProviderApplicationIdLength = 500;
    public const int MaxVersionLength = 100;
    public const int MaxCredentialReferenceLength = 256;
    public const int MaxLeaseOwnerLength = 200;
    public const int MaxExternalProviderMessageIdLength = 500;
    public const int MaxFailureCategoryLength = 100;
    public const int MaxSafeDetailLength = 1024;
    public static readonly TimeSpan MaximumIdempotencyValidity = TimeSpan.FromHours(12);

    private readonly List<WebhookProviderPublicationAttempt> _attempts = [];

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; private set; }
    public Guid WebhookMessageId { get; private set; }
    public WebhookMessage? WebhookMessage { get; private set; }
    public Guid WebhookDeliveryPlanSnapshotId { get; private set; }
    public WebhookDeliveryPlanSnapshot? WebhookDeliveryPlanSnapshot { get; private set; }
    public int ProviderKindId { get; private set; }
    public WebhookProviderKindLookup ProviderKindLookup { get; private set; } = null!;
    [NotMapped]
    public WebhookProviderKind ProviderKind
    {
        get => (WebhookProviderKind)ProviderKindId;
        private set => ProviderKindId = (int)value;
    }
    public Guid ProviderBindingId { get; private set; }
    public WebhookConsumerProviderBinding? ProviderBinding { get; private set; }

    public string ProviderVersion { get; private set; } = string.Empty;
    public string ProviderEventId { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public string ApplicationUid { get; private set; } = string.Empty;
    public string? ProviderApplicationId { get; private set; }
    public string ProviderEnvironment { get; private set; } = string.Empty;
    public string CredentialReference { get; private set; } = string.Empty;
    public string CredentialVersion { get; private set; } = string.Empty;
    public int ModeSnapshotId { get; private set; }
    public WebhookProviderModeLookup ModeSnapshotLookup { get; private set; } = null!;
    [NotMapped]
    public WebhookProviderMode ModeSnapshot
    {
        get => (WebhookProviderMode)ModeSnapshotId;
        private set => ModeSnapshotId = (int)value;
    }
    public string ProviderConfigurationVersion { get; private set; } = string.Empty;
    public int EventContractVersion { get; private set; }
    public string RetentionPolicyVersion { get; private set; } = string.Empty;
    public DateTime PayloadRetentionUntil { get; private set; }
    public DateTime PublicationRetentionUntil { get; private set; }
    public DateTime IdempotencyValidUntil { get; private set; }

    public int StatusId { get; private set; }
    public WebhookProviderPublicationStatusLookup StatusLookup { get; private set; } = null!;
    [NotMapped]
    public WebhookProviderPublicationStatus Status
    {
        get => (WebhookProviderPublicationStatus)StatusId;
        private set => StatusId = (int)value;
    }
    public string? ExternalProviderMessageId { get; private set; }
    public int AutomaticPublicationAttemptCount { get; private set; }
    public int AutomaticReconciliationAttemptCount { get; private set; }
    public DateTime? LastAutomaticReconciliationAt { get; private set; }
    public DateTime? NextActionAt { get; private set; }
    public string? FailureCategory { get; private set; }
    public string? SafeDetail { get; private set; }

    public string? ProcessingLeaseOwner { get; private set; }
    public Guid? ProcessingLeaseToken { get; private set; }
    public DateTime? ProcessingLeaseExpiresAt { get; private set; }
    public DateTime? ProcessingStartedAt { get; private set; }
    public long PublicationFence { get; private set; }
    public long ConcurrencyVersion { get; private set; }

    public DateTime PreparedAt { get; private set; }
    public DateTime? PublishingStartedAt { get; private set; }
    public DateTime? ProviderQueuedAt { get; private set; }
    public DateTime? PublicationUnknownAt { get; private set; }
    public DateTime? DeadLetteredAt { get; private set; }
    public DateTime? ManualReconciliationAt { get; private set; }
    public DateTime? AbandonedAt { get; private set; }

    public IReadOnlyCollection<WebhookProviderPublicationAttempt> Attempts => _attempts;

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    private WebhookProviderPublication()
    {
    }

    public static WebhookProviderPublication Create(
        Guid tenantId,
        Guid webhookMessageId,
        Guid webhookDeliveryPlanSnapshotId,
        WebhookProviderKind providerKind,
        Guid providerBindingId,
        string providerVersion,
        string providerEventId,
        string idempotencyKey,
        string requestHash,
        string applicationUid,
        string providerApplicationId,
        string providerEnvironment,
        string credentialReference,
        string credentialVersion,
        WebhookProviderMode modeSnapshot,
        string providerConfigurationVersion,
        int eventContractVersion,
        string retentionPolicyVersion,
        DateTime payloadRetentionUntil,
        DateTime publicationRetentionUntil,
        DateTime idempotencyValidUntil,
        DateTime preparedAt)
    {
        RequireGuid(tenantId, nameof(tenantId));
        RequireGuid(webhookMessageId, nameof(webhookMessageId));
        RequireGuid(webhookDeliveryPlanSnapshotId, nameof(webhookDeliveryPlanSnapshotId));
        RequireGuid(providerBindingId, nameof(providerBindingId));
        if (!Enum.IsDefined(providerKind))
        {
            throw new ArgumentOutOfRangeException(nameof(providerKind));
        }

        if (!Enum.IsDefined(modeSnapshot))
        {
            throw new ArgumentOutOfRangeException(nameof(modeSnapshot));
        }

        if (eventContractVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(eventContractVersion));
        }

        if (payloadRetentionUntil <= preparedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadRetentionUntil));
        }

        if (publicationRetentionUntil <= preparedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(publicationRetentionUntil));
        }

        if (idempotencyValidUntil <= preparedAt ||
            idempotencyValidUntil - preparedAt > MaximumIdempotencyValidity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(idempotencyValidUntil),
                "Idempotency validity must be positive and cannot exceed twelve hours.");
        }

        return new WebhookProviderPublication
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            WebhookMessageId = webhookMessageId,
            WebhookDeliveryPlanSnapshotId = webhookDeliveryPlanSnapshotId,
            ProviderKind = providerKind,
            ProviderBindingId = providerBindingId,
            ProviderVersion = NormalizeRequired(providerVersion, MaxVersionLength, nameof(providerVersion)),
            ProviderEventId = NormalizeRequired(providerEventId, MaxIdentityLength, nameof(providerEventId)),
            IdempotencyKey = NormalizeRequired(idempotencyKey, MaxIdentityLength, nameof(idempotencyKey)),
            RequestHash = NormalizeHash(requestHash, nameof(requestHash)),
            ApplicationUid = NormalizeRequired(applicationUid, MaxIdentityLength, nameof(applicationUid)),
            ProviderApplicationId = NormalizeRequired(providerApplicationId, MaxIdentityLength, nameof(providerApplicationId)),
            ProviderEnvironment = NormalizeRequired(providerEnvironment, MaxIdentityLength, nameof(providerEnvironment)),
            CredentialReference = NormalizeRequired(credentialReference, MaxCredentialReferenceLength, nameof(credentialReference)),
            CredentialVersion = NormalizeRequired(credentialVersion, MaxVersionLength, nameof(credentialVersion)),
            ModeSnapshot = modeSnapshot,
            ProviderConfigurationVersion = NormalizeRequired(
                providerConfigurationVersion,
                MaxVersionLength,
                nameof(providerConfigurationVersion)),
            EventContractVersion = eventContractVersion,
            RetentionPolicyVersion = NormalizeRequired(
                retentionPolicyVersion,
                MaxVersionLength,
                nameof(retentionPolicyVersion)),
            PayloadRetentionUntil = payloadRetentionUntil,
            PublicationRetentionUntil = publicationRetentionUntil,
            IdempotencyValidUntil = idempotencyValidUntil,
            Status = WebhookProviderPublicationStatus.Prepared,
            ConcurrencyVersion = 1,
            PreparedAt = preparedAt,
            CreatedAt = preparedAt
        };
    }

    public static WebhookProviderPublication CreateLegacy(
        Guid legacyProviderLinkId,
        WebhookMessage message,
        WebhookDeliveryPlanSnapshot deliveryPlan,
        WebhookConsumerProviderBinding providerBinding,
        string? providerApplicationId,
        string? externalProviderMessageId,
        WebhookProviderPublicationStatus status,
        int retryCount,
        string failureCategory,
        string? safeDetail,
        DateTime observedAt)
    {
        RequireGuid(legacyProviderLinkId, nameof(legacyProviderLinkId));
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(deliveryPlan);
        ArgumentNullException.ThrowIfNull(providerBinding);
        if (status is not (WebhookProviderPublicationStatus.ProviderQueued or
            WebhookProviderPublicationStatus.ManualReconciliation or
            WebhookProviderPublicationStatus.Abandoned))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (message.TenantId != deliveryPlan.TenantId ||
            message.TenantId != providerBinding.TenantId ||
            message.Id != deliveryPlan.WebhookMessageId ||
            deliveryPlan.WebhookConsumerId != providerBinding.WebhookConsumerId)
        {
            throw new InvalidOperationException(
                "Legacy provider evidence does not match the persisted tenant, message, plan, and binding.");
        }

        if (observedAt == default)
        {
            throw new ArgumentException("Observation time is required.", nameof(observedAt));
        }

        if (retryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retryCount));
        }

        var preparedAt = deliveryPlan.MaterializedAtUtc.UtcDateTime;
        var payloadRetentionUntil = deliveryPlan.PayloadRetentionUntilUtc.UtcDateTime;
        if (payloadRetentionUntil <= preparedAt)
        {
            payloadRetentionUntil = preparedAt.AddTicks(1);
        }

        var normalizedExternalMessageId = string.IsNullOrWhiteSpace(externalProviderMessageId)
            ? null
            : NormalizeRequired(
                externalProviderMessageId,
                MaxExternalProviderMessageIdLength,
                nameof(externalProviderMessageId));
        if (status == WebhookProviderPublicationStatus.ProviderQueued &&
            normalizedExternalMessageId is null)
        {
            throw new ArgumentException(
                "Evidence-backed provider-queued state requires an external provider message identifier.",
                nameof(externalProviderMessageId));
        }

        var publication = new WebhookProviderPublication
        {
            Id = legacyProviderLinkId,
            TenantId = message.TenantId,
            WebhookMessageId = message.Id,
            WebhookDeliveryPlanSnapshotId = deliveryPlan.Id,
            ProviderKind = providerBinding.ProviderKind,
            ProviderBindingId = providerBinding.Id,
            ProviderVersion = providerBinding.ProviderVersion,
            ProviderEventId = message.Id.ToString("D"),
            IdempotencyKey = message.Id.ToString("D"),
            RequestHash = NormalizeHash(message.PayloadHash, nameof(message)),
            ApplicationUid = providerBinding.ApplicationUid,
            ProviderApplicationId = string.IsNullOrWhiteSpace(providerApplicationId)
                ? null
                : NormalizeRequired(
                    providerApplicationId,
                    MaxProviderApplicationIdLength,
                    nameof(providerApplicationId)),
            ProviderEnvironment = providerBinding.ProviderEnvironment,
            CredentialReference = "legacy-unavailable",
            CredentialVersion = "legacy-unavailable",
            ModeSnapshot = deliveryPlan.ProviderMode,
            ProviderConfigurationVersion = deliveryPlan.ConfigurationVersion,
            EventContractVersion = 1,
            RetentionPolicyVersion = deliveryPlan.RetentionPolicyVersion,
            PayloadRetentionUntil = payloadRetentionUntil,
            PublicationRetentionUntil = payloadRetentionUntil,
            IdempotencyValidUntil = preparedAt.Add(MaximumIdempotencyValidity),
            Status = status,
            ExternalProviderMessageId = normalizedExternalMessageId,
            AutomaticPublicationAttemptCount = retryCount,
            ConcurrencyVersion = 1,
            PreparedAt = preparedAt,
            CreatedAt = preparedAt
        };

        switch (status)
        {
            case WebhookProviderPublicationStatus.ProviderQueued:
                publication.ProviderQueuedAt = observedAt;
                publication.AppendAttempt(
                    WebhookProviderPublicationAttemptOutcome.ProviderQueued,
                    observedAt,
                    normalizedExternalMessageId);
                break;
            case WebhookProviderPublicationStatus.ManualReconciliation:
                publication.ManualReconciliationAt = observedAt;
                publication.SetFailure(failureCategory, safeDetail);
                publication.AppendAttempt(
                    WebhookProviderPublicationAttemptOutcome.ManualReconciliationRequired,
                    observedAt);
                break;
            case WebhookProviderPublicationStatus.Abandoned:
                publication.AbandonedAt = observedAt;
                publication.SetFailure(failureCategory, safeDetail);
                publication.AppendAttempt(WebhookProviderPublicationAttemptOutcome.Abandoned, observedAt);
                break;
        }

        return publication;
    }

    public void ClaimForPublishing(
        string leaseOwner,
        Guid leaseToken,
        DateTime leaseExpiresAt,
        DateTime claimedAt,
        int maxAutomaticPublicationAttempts)
    {
        if (Status is not (WebhookProviderPublicationStatus.Prepared or WebhookProviderPublicationStatus.RetryDue))
        {
            throw new InvalidOperationException($"Publication in state '{Status}' cannot be published.");
        }

        if (NextActionAt is not null && NextActionAt > claimedAt)
        {
            throw new InvalidOperationException("Publication is not due.");
        }

        if (claimedAt >= IdempotencyValidUntil)
        {
            throw new InvalidOperationException("The immutable idempotency validity window has expired.");
        }

        ValidateBound(maxAutomaticPublicationAttempts, nameof(maxAutomaticPublicationAttempts));
        if (AutomaticPublicationAttemptCount >= maxAutomaticPublicationAttempts)
        {
            throw new InvalidOperationException("The automatic publication attempt limit has been reached.");
        }

        SetLease(leaseOwner, leaseToken, leaseExpiresAt, claimedAt);
        Status = WebhookProviderPublicationStatus.Publishing;
        PublishingStartedAt = claimedAt;
        NextActionAt = null;
        AutomaticPublicationAttemptCount = checked(AutomaticPublicationAttemptCount + 1);
        AppendAttempt(WebhookProviderPublicationAttemptOutcome.PublishingStarted, claimedAt);
    }

    public void MarkProviderQueued(
        Guid leaseToken,
        long publicationFence,
        string externalProviderMessageId,
        DateTime queuedAt)
    {
        EnsureActiveLease(leaseToken, publicationFence, queuedAt);
        var wasReconciliation = Status == WebhookProviderPublicationStatus.PublicationUnknown;
        Status = WebhookProviderPublicationStatus.ProviderQueued;
        ExternalProviderMessageId = NormalizeRequired(
            externalProviderMessageId,
            MaxExternalProviderMessageIdLength,
            nameof(externalProviderMessageId));
        ProviderQueuedAt = queuedAt;
        FailureCategory = null;
        SafeDetail = null;
        AppendAttempt(
            wasReconciliation
                ? WebhookProviderPublicationAttemptOutcome.ReconciledProviderQueued
                : WebhookProviderPublicationAttemptOutcome.ProviderQueued,
            queuedAt,
            ExternalProviderMessageId);
        ClearLease();
        AdvanceConcurrencyVersion(queuedAt);
    }

    public void ScheduleRetry(
        Guid leaseToken,
        long publicationFence,
        string failureCategory,
        string? safeDetail,
        DateTime nextActionAt,
        DateTime failedAt)
    {
        EnsurePublishingLease(leaseToken, publicationFence, failedAt);
        if (nextActionAt <= failedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(nextActionAt));
        }

        Status = WebhookProviderPublicationStatus.RetryDue;
        NextActionAt = nextActionAt;
        SetFailure(failureCategory, safeDetail);
        AppendAttempt(WebhookProviderPublicationAttemptOutcome.RetryScheduled, failedAt);
        ClearLease();
        AdvanceConcurrencyVersion(failedAt);
    }

    public void MarkPublicationUnknown(
        Guid leaseToken,
        long publicationFence,
        string failureCategory,
        string? safeDetail,
        DateTime nextActionAt,
        DateTime observedAt)
    {
        EnsurePublishingLease(leaseToken, publicationFence, observedAt);
        if (nextActionAt <= observedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(nextActionAt));
        }

        Status = WebhookProviderPublicationStatus.PublicationUnknown;
        PublicationUnknownAt ??= observedAt;
        NextActionAt = nextActionAt;
        SetFailure(failureCategory, safeDetail);
        AppendAttempt(WebhookProviderPublicationAttemptOutcome.PublicationUnknown, observedAt);
        ClearLease();
        AdvanceConcurrencyVersion(observedAt);
    }

    public void MarkExpiredPublishingUnknown(
        string failureCategory,
        string? safeDetail,
        DateTime nextActionAt,
        DateTime observedAt)
    {
        if (Status != WebhookProviderPublicationStatus.Publishing ||
            ProcessingLeaseExpiresAt is null ||
            ProcessingLeaseExpiresAt > observedAt)
        {
            throw new InvalidOperationException("Only an expired publishing claim can become unknown.");
        }

        if (nextActionAt <= observedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(nextActionAt));
        }

        Status = WebhookProviderPublicationStatus.PublicationUnknown;
        PublicationUnknownAt ??= observedAt;
        NextActionAt = nextActionAt;
        SetFailure(failureCategory, safeDetail);
        AppendAttempt(WebhookProviderPublicationAttemptOutcome.PublicationUnknown, observedAt);
        ClearLease();
        AdvanceConcurrencyVersion(observedAt);
    }

    public void ClaimForAutomaticReconciliation(
        string leaseOwner,
        Guid leaseToken,
        DateTime leaseExpiresAt,
        DateTime claimedAt,
        int maxAutomaticReconciliationAttempts)
    {
        if (Status != WebhookProviderPublicationStatus.PublicationUnknown)
        {
            throw new InvalidOperationException("Only an unknown publication can be reconciled automatically.");
        }

        if (claimedAt >= IdempotencyValidUntil)
        {
            throw new InvalidOperationException("The immutable idempotency validity window has expired.");
        }

        if (NextActionAt is not null && NextActionAt > claimedAt)
        {
            throw new InvalidOperationException("Publication reconciliation is not due.");
        }

        ValidateBound(maxAutomaticReconciliationAttempts, nameof(maxAutomaticReconciliationAttempts));
        if (AutomaticReconciliationAttemptCount >= maxAutomaticReconciliationAttempts)
        {
            throw new InvalidOperationException("The automatic reconciliation limit has been reached.");
        }

        SetLease(leaseOwner, leaseToken, leaseExpiresAt, claimedAt);
        AutomaticReconciliationAttemptCount = checked(AutomaticReconciliationAttemptCount + 1);
        LastAutomaticReconciliationAt = claimedAt;
        NextActionAt = null;
        AppendAttempt(WebhookProviderPublicationAttemptOutcome.AutomaticReconciliationStarted, claimedAt);
    }

    public void RecordAutomaticReconciliationUnresolved(
        Guid leaseToken,
        long publicationFence,
        string failureCategory,
        string? safeDetail,
        DateTime nextActionAt,
        DateTime observedAt)
    {
        EnsureUnknownLease(leaseToken, publicationFence, observedAt);
        if (nextActionAt <= observedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(nextActionAt));
        }

        NextActionAt = nextActionAt;
        SetFailure(failureCategory, safeDetail);
        AppendAttempt(WebhookProviderPublicationAttemptOutcome.AutomaticReconciliationUnresolved, observedAt);
        ClearLease();
        AdvanceConcurrencyVersion(observedAt);
    }

    public void ScheduleRetryAfterConfirmedProviderAbsence(
        Guid leaseToken,
        long publicationFence,
        DateTime nextActionAt,
        DateTime confirmedAt)
    {
        EnsureUnknownLease(leaseToken, publicationFence, confirmedAt);
        if (confirmedAt >= IdempotencyValidUntil)
        {
            throw new InvalidOperationException("Provider absence cannot be retried after idempotency expiry.");
        }

        if (nextActionAt <= confirmedAt || nextActionAt >= IdempotencyValidUntil)
        {
            throw new ArgumentOutOfRangeException(nameof(nextActionAt));
        }

        Status = WebhookProviderPublicationStatus.RetryDue;
        NextActionAt = nextActionAt;
        FailureCategory = null;
        SafeDetail = null;
        AppendAttempt(WebhookProviderPublicationAttemptOutcome.ProviderAbsenceConfirmed, confirmedAt);
        ClearLease();
        AdvanceConcurrencyVersion(confirmedAt);
    }

    public void RequireManualReconciliation(
        string failureCategory,
        string? safeDetail,
        DateTime requiredAt)
    {
        if (Status is not (WebhookProviderPublicationStatus.PublicationUnknown or
            WebhookProviderPublicationStatus.RetryDue or
            WebhookProviderPublicationStatus.DeadLettered))
        {
            throw new InvalidOperationException($"Publication in state '{Status}' cannot require manual reconciliation.");
        }

        Status = WebhookProviderPublicationStatus.ManualReconciliation;
        ManualReconciliationAt = requiredAt;
        NextActionAt = null;
        SetFailure(failureCategory, safeDetail);
        AppendAttempt(WebhookProviderPublicationAttemptOutcome.ManualReconciliationRequired, requiredAt);
        ClearLease();
        AdvanceConcurrencyVersion(requiredAt);
    }

    public void ResolveManuallyAsProviderQueued(string externalProviderMessageId, DateTime resolvedAt)
    {
        if (Status != WebhookProviderPublicationStatus.ManualReconciliation)
        {
            throw new InvalidOperationException("Only a manual-reconciliation publication can be resolved manually.");
        }

        Status = WebhookProviderPublicationStatus.ProviderQueued;
        ExternalProviderMessageId = NormalizeRequired(
            externalProviderMessageId,
            MaxExternalProviderMessageIdLength,
            nameof(externalProviderMessageId));
        ProviderQueuedAt = resolvedAt;
        FailureCategory = null;
        SafeDetail = null;
        AppendAttempt(
            WebhookProviderPublicationAttemptOutcome.ReconciledProviderQueued,
            resolvedAt,
            ExternalProviderMessageId);
        AdvanceConcurrencyVersion(resolvedAt);
    }

    public void DeadLetter(
        Guid leaseToken,
        long publicationFence,
        string failureCategory,
        string? safeDetail,
        DateTime deadLetteredAt)
    {
        EnsurePublishingLease(leaseToken, publicationFence, deadLetteredAt);
        Status = WebhookProviderPublicationStatus.DeadLettered;
        DeadLetteredAt = deadLetteredAt;
        NextActionAt = null;
        SetFailure(failureCategory, safeDetail);
        AppendAttempt(WebhookProviderPublicationAttemptOutcome.DeadLettered, deadLetteredAt);
        ClearLease();
        AdvanceConcurrencyVersion(deadLetteredAt);
    }

    public void Abandon(string failureCategory, string? safeDetail, DateTime abandonedAt)
    {
        if (Status is not (WebhookProviderPublicationStatus.ManualReconciliation or
            WebhookProviderPublicationStatus.DeadLettered))
        {
            throw new InvalidOperationException($"Publication in state '{Status}' cannot be abandoned.");
        }

        Status = WebhookProviderPublicationStatus.Abandoned;
        AbandonedAt = abandonedAt;
        NextActionAt = null;
        SetFailure(failureCategory, safeDetail);
        AppendAttempt(WebhookProviderPublicationAttemptOutcome.Abandoned, abandonedAt);
        ClearLease();
        AdvanceConcurrencyVersion(abandonedAt);
    }

    private void SetLease(string leaseOwner, Guid leaseToken, DateTime leaseExpiresAt, DateTime claimedAt)
    {
        RequireGuid(leaseToken, nameof(leaseToken));
        if (leaseExpiresAt <= claimedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseExpiresAt));
        }

        ProcessingLeaseOwner = NormalizeRequired(leaseOwner, MaxLeaseOwnerLength, nameof(leaseOwner));
        ProcessingLeaseToken = leaseToken;
        ProcessingLeaseExpiresAt = leaseExpiresAt;
        ProcessingStartedAt = claimedAt;
        PublicationFence = checked(PublicationFence + 1);
        AdvanceConcurrencyVersion(claimedAt);
    }

    private void AdvanceConcurrencyVersion(DateTime changedAt)
    {
        ConcurrencyVersion = checked(ConcurrencyVersion + 1);
        UpdatedAt = changedAt;
    }

    private void EnsurePublishingLease(Guid leaseToken, long publicationFence, DateTime observedAt)
    {
        if (Status != WebhookProviderPublicationStatus.Publishing)
        {
            throw new InvalidOperationException("The publication is not actively publishing.");
        }

        EnsureActiveLease(leaseToken, publicationFence, observedAt);
    }

    private void EnsureUnknownLease(Guid leaseToken, long publicationFence, DateTime observedAt)
    {
        if (Status != WebhookProviderPublicationStatus.PublicationUnknown)
        {
            throw new InvalidOperationException("The publication is not awaiting reconciliation.");
        }

        EnsureActiveLease(leaseToken, publicationFence, observedAt);
    }

    private void EnsureActiveLease(Guid leaseToken, long publicationFence, DateTime observedAt)
    {
        if (ProcessingLeaseToken != leaseToken ||
            PublicationFence != publicationFence ||
            ProcessingLeaseExpiresAt is null ||
            ProcessingLeaseExpiresAt <= observedAt)
        {
            throw new InvalidOperationException("The provider publication claim is stale or no longer active.");
        }
    }

    private void SetFailure(string failureCategory, string? safeDetail)
    {
        FailureCategory = NormalizeRequired(
            failureCategory,
            MaxFailureCategoryLength,
            nameof(failureCategory));
        SafeDetail = BoundSafeDetail(safeDetail);
    }

    private void AppendAttempt(
        WebhookProviderPublicationAttemptOutcome outcome,
        DateTime recordedAt,
        string? externalProviderMessageId = null)
    {
        _attempts.Add(WebhookProviderPublicationAttempt.Create(
            TenantId,
            Id,
            checked(_attempts.Count + 1),
            PublicationFence,
            outcome,
            ProcessingStartedAt ?? recordedAt,
            recordedAt,
            externalProviderMessageId,
            FailureCategory,
            SafeDetail));
    }

    private void ClearLease()
    {
        ProcessingLeaseOwner = null;
        ProcessingLeaseToken = null;
        ProcessingLeaseExpiresAt = null;
        ProcessingStartedAt = null;
    }

    private static string NormalizeHash(string hash, string parameterName)
    {
        var normalized = NormalizeRequired(hash, 71, parameterName).ToLowerInvariant();
        if (!normalized.StartsWith("sha256:", StringComparison.Ordinal) ||
            normalized.Length != 71 ||
            normalized.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") >= 0)
        {
            throw new ArgumentException("Request hash must be a lowercase SHA-256 identifier.", parameterName);
        }

        return normalized;
    }

    internal static string NormalizeRequired(string value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maxLength} characters.");
        }

        return normalized;
    }

    internal static string? NormalizeOptional(string? value, int maxLength, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? null : NormalizeRequired(value, maxLength, parameterName);

    internal static string? BoundSafeDetail(string? safeDetail)
    {
        if (string.IsNullOrWhiteSpace(safeDetail))
        {
            return null;
        }

        var normalized = safeDetail.Trim();
        return normalized.Length <= MaxSafeDetailLength
            ? normalized
            : normalized[..MaxSafeDetailLength];
    }

    private static void ValidateBound(int value, string parameterName)
    {
        if (value < 1)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void RequireGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier is required.", parameterName);
        }
    }
}
