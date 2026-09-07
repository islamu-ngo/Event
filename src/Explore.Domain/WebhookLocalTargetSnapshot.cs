using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class WebhookLocalTargetSnapshot : ITenantEntity, IAuditableEntity
{
    public const int MaxDestinationUrlLength = 2_048;
    public const int MaxCredentialReferenceLength = 500;
    public const int MaxLeaseOwnerLength = 200;

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; private set; } = null!;
    public Guid WebhookMessageId { get; private set; }
    public WebhookMessage WebhookMessage { get; private set; } = null!;
    public Guid DeliveryPlanSnapshotId { get; private set; }
    public WebhookDeliveryPlanSnapshot DeliveryPlanSnapshot { get; private set; } = null!;
    public Guid WebhookEndpointId { get; private set; }
    public WebhookEndpoint WebhookEndpoint { get; private set; } = null!;
    public int EndpointConfigurationVersion { get; private set; }
    public string DestinationUrl { get; private set; } = string.Empty;
    public string CredentialReference { get; private set; } = string.Empty;
    public int CredentialVersion { get; private set; }
    public DateTimeOffset CredentialValidFromUtc { get; private set; }
    public DateTimeOffset? CredentialValidUntilUtc { get; private set; }
    public int MaxAttempts { get; private set; }
    public int TimeoutSeconds { get; private set; }
    public int? RateLimitPerMinute { get; private set; }
    public DateTimeOffset CapturedAtUtc { get; private set; }
    public int DeliveryStatusId { get; private set; }
    public WebhookLocalDeliveryStatusLookup DeliveryStatusLookup { get; private set; } = null!;
    [NotMapped]
    public WebhookLocalDeliveryStatus DeliveryStatus
    {
        get => (WebhookLocalDeliveryStatus)DeliveryStatusId;
        private set => DeliveryStatusId = (int)value;
    }
    public DateTimeOffset NextActionAtUtc { get; private set; }
    public string? ProcessingLeaseOwner { get; private set; }
    public Guid? ProcessingLeaseToken { get; private set; }
    public DateTimeOffset? ProcessingLeaseExpiresAtUtc { get; private set; }
    public long DeliveryFence { get; private set; }
    public long ConcurrencyVersion { get; private set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static WebhookLocalTargetSnapshot Create(
        WebhookDeliveryPlanSnapshot deliveryPlanSnapshot,
        WebhookEndpoint webhookEndpoint,
        int endpointConfigurationVersion,
        DateTimeOffset credentialValidFromUtc,
        DateTimeOffset? credentialValidUntilUtc,
        DateTimeOffset capturedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(deliveryPlanSnapshot);
        ArgumentNullException.ThrowIfNull(webhookEndpoint);

        if (deliveryPlanSnapshot.ProviderMode is not (WebhookProviderMode.Local or WebhookProviderMode.Composite))
        {
            throw new InvalidOperationException(
                "A local target requires a Local or Composite delivery plan.");
        }

        var endpointCanReceiveSourceTenant =
            webhookEndpoint.TenantId == deliveryPlanSnapshot.TenantId ||
            webhookEndpoint.TenantId is null && webhookEndpoint.InstanceId.HasValue;
        if (!endpointCanReceiveSourceTenant ||
            webhookEndpoint.ConsumerId != deliveryPlanSnapshot.WebhookConsumerId)
        {
            throw new InvalidOperationException(
                "The endpoint must belong to the delivery plan consumer and support its source tenant.");
        }

        if (webhookEndpoint.Status != WebhookEndpointStatus.Active)
        {
            throw new InvalidOperationException("Only an active endpoint can be snapshotted for local delivery.");
        }

        if (endpointConfigurationVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(endpointConfigurationVersion));
        }

        if (webhookEndpoint.SecretVersion < 1)
        {
            throw new InvalidOperationException("The endpoint signing credential version is invalid.");
        }

        if (webhookEndpoint.MaxAttempts < 1)
        {
            throw new InvalidOperationException("The endpoint must allow at least one delivery attempt.");
        }

        if (webhookEndpoint.TimeoutSeconds < 1)
        {
            throw new InvalidOperationException("The endpoint timeout must be positive.");
        }

        if (webhookEndpoint.RateLimitPerMinute is < 1)
        {
            throw new InvalidOperationException("The endpoint rate limit must be positive when configured.");
        }

        WebhookDeliveryPlanSnapshot.RequireTimestamp(capturedAtUtc, nameof(capturedAtUtc));
        WebhookDeliveryPlanSnapshot.RequireTimestamp(
            credentialValidFromUtc,
            nameof(credentialValidFromUtc));

        if (credentialValidUntilUtc is { } validUntilUtc)
        {
            WebhookDeliveryPlanSnapshot.RequireTimestamp(validUntilUtc, nameof(credentialValidUntilUtc));
            if (validUntilUtc <= credentialValidFromUtc)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(credentialValidUntilUtc),
                    "Credential validity must end after it begins.");
            }
        }

        return new WebhookLocalTargetSnapshot
        {
            Id = Guid.CreateVersion7(),
            TenantId = deliveryPlanSnapshot.TenantId,
            WebhookMessageId = deliveryPlanSnapshot.WebhookMessageId,
            DeliveryPlanSnapshotId = deliveryPlanSnapshot.Id,
            WebhookEndpointId = webhookEndpoint.Id,
            EndpointConfigurationVersion = endpointConfigurationVersion,
            DestinationUrl = NormalizeDestinationUrl(webhookEndpoint.Url),
            CredentialReference = WebhookDeliveryPlanSnapshot.NormalizeRequired(
                webhookEndpoint.SecretRef,
                MaxCredentialReferenceLength,
                nameof(webhookEndpoint.SecretRef)),
            CredentialVersion = webhookEndpoint.SecretVersion,
            CredentialValidFromUtc = credentialValidFromUtc,
            CredentialValidUntilUtc = credentialValidUntilUtc,
            MaxAttempts = webhookEndpoint.MaxAttempts,
            TimeoutSeconds = webhookEndpoint.TimeoutSeconds,
            RateLimitPerMinute = webhookEndpoint.RateLimitPerMinute,
            CapturedAtUtc = capturedAtUtc,
            DeliveryStatus = WebhookLocalDeliveryStatus.Pending,
            NextActionAtUtc = capturedAtUtc,
            ConcurrencyVersion = 1,
            CreatedAt = capturedAtUtc.UtcDateTime
        };
    }

    public void ClaimForDelivery(
        string leaseOwner,
        Guid leaseToken,
        DateTimeOffset leaseExpiresAtUtc,
        DateTimeOffset claimedAtUtc)
    {
        if (DeliveryStatus is not (WebhookLocalDeliveryStatus.Pending or WebhookLocalDeliveryStatus.RetryDue))
        {
            throw new InvalidOperationException($"Local target in state '{DeliveryStatus}' cannot be delivered.");
        }

        if (NextActionAtUtc > claimedAtUtc)
        {
            throw new InvalidOperationException("Local target is not due.");
        }

        if (leaseToken == Guid.Empty)
        {
            throw new ArgumentException("Lease token is required.", nameof(leaseToken));
        }

        if (leaseExpiresAtUtc <= claimedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseExpiresAtUtc));
        }

        ProcessingLeaseOwner = WebhookDeliveryPlanSnapshot.NormalizeRequired(
            leaseOwner,
            MaxLeaseOwnerLength,
            nameof(leaseOwner));
        ProcessingLeaseToken = leaseToken;
        ProcessingLeaseExpiresAtUtc = leaseExpiresAtUtc;
        DeliveryFence = checked(DeliveryFence + 1);
        DeliveryStatus = WebhookLocalDeliveryStatus.Delivering;
        AdvanceConcurrencyVersion(claimedAtUtc);
    }

    public void MarkSucceeded(Guid leaseToken, long deliveryFence, DateTimeOffset completedAtUtc)
    {
        EnsureActiveLease(leaseToken, deliveryFence, completedAtUtc);
        DeliveryStatus = WebhookLocalDeliveryStatus.Succeeded;
        NextActionAtUtc = completedAtUtc;
        ClearLease();
        AdvanceConcurrencyVersion(completedAtUtc);
    }

    public void ScheduleRetry(
        Guid leaseToken,
        long deliveryFence,
        DateTimeOffset nextActionAtUtc,
        DateTimeOffset failedAtUtc)
    {
        EnsureActiveLease(leaseToken, deliveryFence, failedAtUtc);
        if (nextActionAtUtc <= failedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(nextActionAtUtc));
        }

        DeliveryStatus = WebhookLocalDeliveryStatus.RetryDue;
        NextActionAtUtc = nextActionAtUtc;
        ClearLease();
        AdvanceConcurrencyVersion(failedAtUtc);
    }

    public void DeadLetter(Guid leaseToken, long deliveryFence, DateTimeOffset deadLetteredAtUtc)
    {
        EnsureActiveLease(leaseToken, deliveryFence, deadLetteredAtUtc);
        DeliveryStatus = WebhookLocalDeliveryStatus.DeadLettered;
        NextActionAtUtc = deadLetteredAtUtc;
        ClearLease();
        AdvanceConcurrencyVersion(deadLetteredAtUtc);
    }

    public void Abandon(Guid leaseToken, long deliveryFence, DateTimeOffset abandonedAtUtc)
    {
        EnsureActiveLease(leaseToken, deliveryFence, abandonedAtUtc);
        DeliveryStatus = WebhookLocalDeliveryStatus.Abandoned;
        NextActionAtUtc = abandonedAtUtc;
        ClearLease();
        AdvanceConcurrencyVersion(abandonedAtUtc);
    }

    public void RecoverExpiredClaim(DateTimeOffset recoveredAtUtc)
    {
        if (DeliveryStatus != WebhookLocalDeliveryStatus.Delivering ||
            ProcessingLeaseExpiresAtUtc is null ||
            ProcessingLeaseExpiresAtUtc > recoveredAtUtc)
        {
            throw new InvalidOperationException("Only an expired Local delivery claim can be recovered.");
        }

        DeliveryStatus = DeliveryFence >= MaxAttempts
            ? WebhookLocalDeliveryStatus.DeadLettered
            : WebhookLocalDeliveryStatus.RetryDue;
        NextActionAtUtc = recoveredAtUtc;
        ClearLease();
        AdvanceConcurrencyVersion(recoveredAtUtc);
    }

    public void ScheduleManualRetry(DateTimeOffset retryAtUtc)
    {
        if (DeliveryStatus is not (WebhookLocalDeliveryStatus.DeadLettered or WebhookLocalDeliveryStatus.Abandoned))
        {
            throw new InvalidOperationException("Only terminal Local targets can be retried manually.");
        }

        DeliveryStatus = WebhookLocalDeliveryStatus.RetryDue;
        NextActionAtUtc = retryAtUtc;
        ClearLease();
        AdvanceConcurrencyVersion(retryAtUtc);
    }

    public void MigratePendingConfiguration(
        WebhookEndpoint webhookEndpoint,
        DateTimeOffset migratedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(webhookEndpoint);
        WebhookDeliveryPlanSnapshot.RequireTimestamp(migratedAtUtc, nameof(migratedAtUtc));
        if (DeliveryStatus != WebhookLocalDeliveryStatus.Pending ||
            ProcessingLeaseToken is not null ||
            ProcessingLeaseExpiresAtUtc is not null ||
            DeliveryFence != 0)
        {
            throw new InvalidOperationException(
                "Only unclaimed pending Local work can migrate to a new endpoint configuration.");
        }

        var endpointCanReceiveSourceTenant =
            webhookEndpoint.TenantId == TenantId ||
            webhookEndpoint.TenantId is null && webhookEndpoint.InstanceId.HasValue;
        if (webhookEndpoint.Id != WebhookEndpointId || !endpointCanReceiveSourceTenant)
        {
            throw new InvalidOperationException(
                "The endpoint configuration must match the pending target endpoint and source tenant.");
        }

        if (webhookEndpoint.ConfigurationVersion <= EndpointConfigurationVersion)
        {
            throw new InvalidOperationException(
                "The endpoint configuration migration must advance the snapshotted version.");
        }

        var credentialChanged = webhookEndpoint.SecretVersion != CredentialVersion ||
            !string.Equals(webhookEndpoint.SecretRef, CredentialReference, StringComparison.Ordinal);
        EndpointConfigurationVersion = webhookEndpoint.ConfigurationVersion;
        DestinationUrl = NormalizeDestinationUrl(webhookEndpoint.Url);
        CredentialReference = WebhookDeliveryPlanSnapshot.NormalizeRequired(
            webhookEndpoint.SecretRef,
            MaxCredentialReferenceLength,
            nameof(webhookEndpoint.SecretRef));
        CredentialVersion = webhookEndpoint.SecretVersion;
        if (credentialChanged)
        {
            if (webhookEndpoint.SecretActivatedAt == default ||
                webhookEndpoint.SecretActivatedAt.Kind != DateTimeKind.Utc)
            {
                throw new InvalidOperationException(
                    "Credential migration requires an authoritative UTC activation timestamp.");
            }

            CredentialValidFromUtc = new DateTimeOffset(webhookEndpoint.SecretActivatedAt);
            CredentialValidUntilUtc = null;
        }

        MaxAttempts = webhookEndpoint.MaxAttempts;
        TimeoutSeconds = webhookEndpoint.TimeoutSeconds;
        RateLimitPerMinute = webhookEndpoint.RateLimitPerMinute;
        CapturedAtUtc = migratedAtUtc;
        ConcurrencyVersion = checked(ConcurrencyVersion + 1);
        UpdatedAt = migratedAtUtc.UtcDateTime;
    }

    private static string NormalizeDestinationUrl(string destinationUrl)
    {
        var normalized = WebhookDeliveryPlanSnapshot.NormalizeRequired(
            destinationUrl,
            MaxDestinationUrlLength,
            nameof(destinationUrl));

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var destinationUri) ||
            (destinationUri.Scheme != Uri.UriSchemeHttp && destinationUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "Webhook destination must be an absolute HTTP or HTTPS URL.",
                nameof(destinationUrl));
        }

        return normalized;
    }

    private void EnsureActiveLease(
        Guid leaseToken,
        long deliveryFence,
        DateTimeOffset observedAtUtc)
    {
        if (DeliveryStatus != WebhookLocalDeliveryStatus.Delivering ||
            ProcessingLeaseToken != leaseToken ||
            DeliveryFence != deliveryFence ||
            ProcessingLeaseExpiresAtUtc is null ||
            ProcessingLeaseExpiresAtUtc <= observedAtUtc)
        {
            throw new InvalidOperationException("The Local delivery claim is stale or no longer active.");
        }
    }

    private void ClearLease()
    {
        ProcessingLeaseOwner = null;
        ProcessingLeaseToken = null;
        ProcessingLeaseExpiresAtUtc = null;
    }

    private void AdvanceConcurrencyVersion(DateTimeOffset changedAtUtc)
    {
        ConcurrencyVersion = checked(ConcurrencyVersion + 1);
        UpdatedAt = changedAtUtc.UtcDateTime;
    }
}
