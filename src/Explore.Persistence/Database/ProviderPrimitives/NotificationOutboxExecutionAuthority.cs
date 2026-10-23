using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Persistence.Database.ProviderPrimitives;

public sealed class NotificationOutboxExecutionAuthority(
    ExploreDbContext context,
    INotificationOutboxAdmission admission) : INotificationOutboxExecutionAuthority
{
    public async Task BindAsync(NotificationOutboxClaim claim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (context.NotificationOutboxExecutionClaim is not null || context.IsTenantFilterBypassed || claim.TenantId == Guid.Empty
            || context.BindTenantLease() != claim.TenantId
            || await admission.ReadClaimAsync(claim.MessageId, claim.ProcessingLeaseExpiresAt, cancellationToken) != claim)
            throw new InvalidOperationException("notification_outbox_execution_not_admitted");
        context.BindOutboxExecutionClaim(claim);
    }

    public async Task RequireAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        Guid tenantId = context.BindTenantLease() ?? Guid.Empty;
        if (tenantId == Guid.Empty || context.IsTenantFilterBypassed
            || message.Id == Guid.Empty || message.Status != OutboxMessageStatus.Processing
            || message.NextRetryAt is not { } lease || message.Payload is null
            || !NotificationOutboxRoutes.Contains(message.EventType))
            throw new InvalidOperationException("notification_outbox_execution_not_admitted");

        NotificationOutboxClaim? claim = await admission.ReadClaimAsync(message.Id, lease, cancellationToken);
        if (context.NotificationOutboxExecutionClaim is null || claim != context.NotificationOutboxExecutionClaim
            || claim.TenantId != tenantId || claim.AggregateId != message.AggregateId
            || claim.AggregateType != message.AggregateType || claim.EventType != message.EventType
            || !string.Equals(claim.PayloadDigest,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message.Payload))),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("notification_outbox_execution_not_admitted");
    }
}
