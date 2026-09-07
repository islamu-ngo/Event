using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class WebPushSubscription : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public required string DeviceIdentifier { get; set; }
    public required string Endpoint { get; set; }
    public required string P256Dh { get; set; }
    public required string AuthSecret { get; set; }
    public DateTime? ExpirationTime { get; set; }
    public bool IsActive { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime? UnsubscribedAt { get; set; }
    public DateTime? DeactivatedAt { get; set; }
    public string? DeactivationReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    public static WebPushSubscription Create(
        Guid tenantId,
        Guid userId,
        string deviceIdentifier,
        string endpoint,
        string p256Dh,
        string authSecret,
        DateTime? expirationTime,
        DateTime now)
    {
        Validate(deviceIdentifier, endpoint, p256Dh, authSecret);

        return new WebPushSubscription
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null,
            UserId = userId,
            User = null,
            DeviceIdentifier = deviceIdentifier.Trim(),
            Endpoint = endpoint.Trim(),
            P256Dh = p256Dh.Trim(),
            AuthSecret = authSecret.Trim(),
            ExpirationTime = expirationTime,
            IsActive = true,
            CreatedAt = now,
            LastSeenAt = now,
        };
    }

    public void Touch(
        string endpoint,
        string p256Dh,
        string authSecret,
        DateTime? expirationTime,
        DateTime now)
    {
        EnsureActive();
        Validate(DeviceIdentifier, endpoint, p256Dh, authSecret);

        Endpoint = endpoint.Trim();
        P256Dh = p256Dh.Trim();
        AuthSecret = authSecret.Trim();
        ExpirationTime = expirationTime;
        LastSeenAt = now;
        UpdatedAt = now;
    }

    public void Unsubscribe(DateTime now)
    {
        EnsureActive();
        IsActive = false;
        UnsubscribedAt = now;
        UpdatedAt = now;
    }

    public void Deactivate(string reason, DateTime now)
    {
        EnsureActive();
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Deactivation reason is required.", nameof(reason));
        }

        IsActive = false;
        DeactivatedAt = now;
        DeactivationReason = reason.Trim();
        UpdatedAt = now;
    }

    private void EnsureActive()
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("Web Push subscription is no longer active.");
        }
    }

    private static void Validate(string deviceIdentifier, string endpoint, string p256Dh, string authSecret)
    {
        if (string.IsNullOrWhiteSpace(deviceIdentifier)) throw new ArgumentException("Device identifier is required.", nameof(deviceIdentifier));
        if (string.IsNullOrWhiteSpace(endpoint)) throw new ArgumentException("Endpoint is required.", nameof(endpoint));
        if (string.IsNullOrWhiteSpace(p256Dh)) throw new ArgumentException("P-256 DH key is required.", nameof(p256Dh));
        if (string.IsNullOrWhiteSpace(authSecret)) throw new ArgumentException("Auth secret is required.", nameof(authSecret));
    }
}
