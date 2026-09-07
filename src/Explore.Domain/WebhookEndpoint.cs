using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class WebhookEndpoint : IAuditableEntity
{
    private Guid _configurationScopeId;

    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public Guid? InstanceId { get; set; }
    public InstanceBootstrapState? Instance { get; set; }
    public Guid ConfigurationScopeId
    {
        get => TenantId ?? InstanceId ?? _configurationScopeId;
        private set => _configurationScopeId = value;
    }

    public Guid ConsumerId { get; set; }
    public WebhookConsumer? Consumer { get; set; }

    public required string Url { get; set; }
    public string? Description { get; set; }
    public int StatusId { get; set; }
    public WebhookEndpointStatusLookup StatusLookup { get; set; } = null!;
    [NotMapped]
    public WebhookEndpointStatus Status
    {
        get => (WebhookEndpointStatus)StatusId;
        set => StatusId = (int)value;
    }
    public required string SecretRef { get; set; }
    public int SecretVersion { get; set; }
    public DateTime SecretActivatedAt { get; set; }
    public string? PreviousSecretRef { get; set; }
    public DateTime? PreviousSecretValidUntil { get; set; }
    public string? ProviderEndpointId { get; set; }
    public int MaxAttempts { get; set; }
    public int TimeoutSeconds { get; set; }
    public int? RateLimitPerMinute { get; set; }
    public int ConfigurationVersion { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public DateTime? LastFailureAt { get; set; }
    public int ConsecutiveFailureCount { get; set; }
    public DateTime? CircuitOpenedAt { get; set; }
    public DateTime? AutoPausedAt { get; set; }
    public string? AutoPauseReason { get; set; }
    public DateTime? LastResumedAt { get; set; }
    public Guid? LastResumedBy { get; set; }
    public long DeliveryStateVersion { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public List<WebhookEndpointSubscription> Subscriptions { get; } = [];

    public void UpdateConfiguration(
        string url,
        string? description,
        int maxAttempts,
        int timeoutSeconds,
        int? rateLimitPerMinute,
        DateTime updatedAt)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("Endpoint URL is required.", nameof(url));
        }

        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        }

        if (timeoutSeconds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        if (rateLimitPerMinute is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rateLimitPerMinute));
        }

        EnsureUtc(updatedAt, nameof(updatedAt));
        Url = url.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        MaxAttempts = maxAttempts;
        TimeoutSeconds = timeoutSeconds;
        RateLimitPerMinute = rateLimitPerMinute;
        ConfigurationVersion = checked(ConfigurationVersion + 1);
        UpdatedAt = updatedAt;
    }

    public void RotateSigningCredential(
        string secretReference,
        DateTime? previousSecretValidUntil,
        DateTime updatedAt)
    {
        if (string.IsNullOrWhiteSpace(secretReference))
        {
            throw new ArgumentException("Secret reference is required.", nameof(secretReference));
        }

        EnsureUtc(updatedAt, nameof(updatedAt));
        if (previousSecretValidUntil is { } validUntil)
        {
            EnsureUtc(validUntil, nameof(previousSecretValidUntil));
            if (validUntil < updatedAt)
            {
                throw new ArgumentOutOfRangeException(nameof(previousSecretValidUntil));
            }
        }

        PreviousSecretRef = SecretRef;
        PreviousSecretValidUntil = previousSecretValidUntil;
        SecretRef = secretReference.Trim();
        SecretVersion = checked(SecretVersion + 1);
        SecretActivatedAt = updatedAt;
        ConfigurationVersion = checked(ConfigurationVersion + 1);
        UpdatedAt = updatedAt;
    }

    public bool RecordFailure(DateTime failedAt, string failureCategory, int autoPauseThreshold)
    {
        if (autoPauseThreshold < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(autoPauseThreshold));
        }

        LastFailureAt = failedAt;
        UpdatedAt = failedAt;

        if (Status != WebhookEndpointStatus.Active)
        {
            return false;
        }

        ConsecutiveFailureCount++;
        DeliveryStateVersion++;
        if (ConsecutiveFailureCount < autoPauseThreshold)
        {
            return false;
        }

        Status = WebhookEndpointStatus.AutoPaused;
        CircuitOpenedAt = failedAt;
        AutoPausedAt = failedAt;
        AutoPauseReason = failureCategory;
        return true;
    }

    public void RecordSuccess(DateTime succeededAt)
    {
        LastSuccessAt = succeededAt;
        UpdatedAt = succeededAt;
        if (Status == WebhookEndpointStatus.Active)
        {
            ConsecutiveFailureCount = 0;
            DeliveryStateVersion++;
        }
    }

    public bool Resume(DateTime resumedAt, Guid actorUserId)
    {
        if (Status is not (WebhookEndpointStatus.AutoPaused or WebhookEndpointStatus.Disabled))
        {
            return false;
        }

        Status = WebhookEndpointStatus.Active;
        ConsecutiveFailureCount = 0;
        CircuitOpenedAt = null;
        AutoPausedAt = null;
        AutoPauseReason = null;
        LastResumedAt = resumedAt;
        LastResumedBy = actorUserId;
        UpdatedAt = resumedAt;
        UpdatedBy = actorUserId;
        DeliveryStateVersion++;
        return true;
    }

    public bool Pause(DateTime pausedAt, Guid actorUserId)
    {
        EnsureUtc(pausedAt, nameof(pausedAt));
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("Actor user id is required.", nameof(actorUserId));
        }

        if (Status != WebhookEndpointStatus.Active)
        {
            return false;
        }

        Status = WebhookEndpointStatus.Disabled;
        UpdatedAt = pausedAt;
        UpdatedBy = actorUserId;
        DeliveryStateVersion++;
        return true;
    }

    private static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must use UTC kind.", parameterName);
        }
    }
}
