using Explore.Application.Settings.Groups;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Purpose-bound queue admission returns only persisted owners and fencing receipts, never payloads or credentials.</summary>
public interface IWebhookLocalDeliveryAdmission
{
    Task<IReadOnlyList<Guid>> GetDueTenantIdsAsync(int tenantLimit, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<int> CountDueAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<int> CountStaleDeliveringAsync(DateTimeOffset observedAtUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<WebhookLocalDeliveryClaim>> ClaimDueAsync(
        WebhookLocalTargetClaimRequest request,
        IReadOnlyDictionary<Guid, WebhookDeliveryClaimLimits> tenantLimits,
        CancellationToken cancellationToken);
    Task<int> RecoverExpiredClaimsAsync(
        DateTimeOffset recoveredAtUtc, string failureCategory, int batchSize, CancellationToken cancellationToken);
    Task<WebhookDeliverySettingGroup> GetGovernanceSettingsAsync(Guid? tenantId, CancellationToken cancellationToken);
}

public sealed record WebhookLocalDeliveryClaim(
    Guid TenantId,
    Guid TargetId,
    Guid LeaseToken,
    long DeliveryFence,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset LeaseExpiresAtUtc,
    DateTimeOffset NextActionAtUtc);
