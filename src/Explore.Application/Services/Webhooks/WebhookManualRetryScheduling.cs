using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Telemetry;
using Explore.Domain;

namespace Explore.Application.Services.Webhooks;

/// <summary>
/// One scheduling implementation for explicit worker ownership and prepared selected human
/// ownership. Callers supply their own original repositories and transaction; this core
/// neither obtains authority nor opens a scope, transaction or external transport.
/// </summary>
public static class WebhookManualRetryScheduling
{
    public static async Task<WebhookDeliverySingleDrainResult> ScheduleAsync(
        Guid tenantId,
        Guid attemptId,
        WebhookAuditPrincipalKind principalKind,
        string principalReference,
        IWebhookDeliveryAttemptRepository attempts,
        IWebhookLocalTargetRepository targets,
        IWebhookAuditEventWriter audit,
        bool localProcessingEnabled,
        TimeProvider clock,
        BusinessMetrics metrics,
        CancellationToken cancellationToken)
    {
        var attempt = await attempts.GetByTenantAndIdAsync(tenantId, attemptId, cancellationToken);
        WebhookDeliverySingleDrainResult Result(WebhookDeliveryDrainOutcome outcome, string metricOutcome, string? category)
        {
            metrics.RecordWebhookManualRetry(attempt?.Message?.EventType, metricOutcome, category);
            return new(outcome, attempt?.Id);
        }
        if (attempt is null)
            return Result(WebhookDeliveryDrainOutcome.Missing, "missing", "missing_message");
        if (!localProcessingEnabled)
            return Result(WebhookDeliveryDrainOutcome.Skipped, "skipped", "provider_disabled");
        if (attempt.Outcome == WebhookDeliveryAttemptOutcome.Succeeded)
            return Result(WebhookDeliveryDrainOutcome.AlreadySettled, "already_settled", "attempt_status_not_retryable");
        if (attempt.Outcome is WebhookDeliveryAttemptOutcome.Scheduled or WebhookDeliveryAttemptOutcome.Sending)
            return Result(WebhookDeliveryDrainOutcome.Deferred, "deferred", "attempt_status_not_retryable");
        if (attempt.Endpoint is null || attempt.Message is null)
            return Result(WebhookDeliveryDrainOutcome.Missing, "missing", "missing_message");
        if (attempt.Endpoint.Status != WebhookEndpointStatus.Active)
            return Result(WebhookDeliveryDrainOutcome.Skipped, "skipped", "endpoint_not_active");

        var retryRequestedAt = clock.GetUtcNow();
        if (attempt.Message.PayloadRetentionUntil <= retryRequestedAt.UtcDateTime
            || attempt.Message.GetPayloadBytes() is null)
            return Result(WebhookDeliveryDrainOutcome.Skipped, "skipped", "message_payload_retention_expired");

        var target = await targets.GetByMessageAndEndpointForUpdateAsync(
            tenantId, attempt.MessageId, attempt.EndpointId, cancellationToken);
        if (target is null)
            return Result(WebhookDeliveryDrainOutcome.Missing, "skipped", "attempt_status_not_retryable");
        if (target.DeliveryStatus == WebhookLocalDeliveryStatus.Delivering)
            return Result(WebhookDeliveryDrainOutcome.AlreadyClaimed, "skipped", "attempt_status_not_retryable");
        if (target.DeliveryStatus is WebhookLocalDeliveryStatus.Pending or WebhookLocalDeliveryStatus.RetryDue)
            return Result(WebhookDeliveryDrainOutcome.Deferred, "skipped", "attempt_status_not_retryable");
        if (target.DeliveryStatus == WebhookLocalDeliveryStatus.Succeeded)
            return Result(WebhookDeliveryDrainOutcome.AlreadySettled, "skipped", "attempt_status_not_retryable");

        var previousTargetStatus = target.DeliveryStatus;
        target.ScheduleManualRetry(retryRequestedAt);
        await targets.SaveChangesAsync(cancellationToken);
        await audit.AppendAsync(new WebhookAuditWriteRequest(
            attempt.Endpoint.Consumer is null ? tenantId : attempt.Endpoint.Consumer.TenantId,
            WebhookAuditAction.DeliveryRetryScheduled,
            WebhookAuditTargetKind.DeliveryAttempt,
            attempt.Id,
            "manual_retry",
            WebhookAuditOutcome.Succeeded,
            SafeBeforeJson: JsonSerializer.Serialize(new
            {
                sourceAttemptId = attempt.Id,
                sourceOutcome = attempt.Outcome.ToString(),
                targetStatus = previousTargetStatus.ToString()
            }),
            SafeAfterJson: JsonSerializer.Serialize(new
            {
                localTargetId = target.Id,
                target.WebhookMessageId,
                target.WebhookEndpointId,
                targetStatus = target.DeliveryStatus.ToString(),
                target.NextActionAtUtc
            }),
            ConfigurationVersion: $"endpoint-v{target.EndpointConfigurationVersion}:target-v{target.ConcurrencyVersion}",
            EffectiveScopeKind: attempt.Endpoint.Consumer?.Ownership.AuditScopeKind ?? WebhookAuditScopeKind.Tenant,
            EffectiveScopeId: attempt.Endpoint.Consumer?.OwnerId ?? tenantId,
            PrincipalKind: principalKind,
            PrincipalReference: principalReference), cancellationToken);
        return Result(WebhookDeliveryDrainOutcome.RetryScheduled, "retry_scheduled", null);
    }
}
