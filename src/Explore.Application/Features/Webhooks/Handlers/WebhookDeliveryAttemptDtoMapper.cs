using Explore.Application.DTOs.Webhooks;
using Explore.Application.Lookups;
using Explore.Domain;

namespace Explore.Application.Features.Webhooks.Handlers;

internal static class WebhookDeliveryAttemptDtoMapper
{
    public static WebhookDeliveryAttemptDto Map(WebhookDeliveryAttempt attempt)
    {
        var endpointStatus = NormalizedLookupMetadata.WebhookEndpointStatus(
            attempt.Endpoint?.StatusId ?? (int)WebhookEndpointStatus.Archived);
        var outcome = NormalizedLookupMetadata.WebhookDeliveryAttemptOutcome(attempt.OutcomeId);
        var consumer = attempt.Endpoint?.Consumer;
        return new()
        {
            Id = attempt.Id,
            TenantId = attempt.TenantId,
            OwnerKindId = consumer?.ConsumerKindId ?? (int)WebhookConsumerKind.Tenant,
            OwnerId = consumer?.OwnerId ?? attempt.TenantId,
            MessageId = attempt.MessageId,
            MessageEventType = attempt.Message?.EventType,
            EndpointId = attempt.EndpointId,
            EndpointStatusId = endpointStatus.Id,
            EndpointStatusCode = endpointStatus.Code,
            EndpointStatusName = endpointStatus.Name,
            AttemptNumber = attempt.AttemptNumber,
            OutcomeId = outcome.Id,
            OutcomeCode = outcome.Code,
            OutcomeName = outcome.Name,
            ScheduledAt = attempt.ScheduledAt,
            SentAt = attempt.SentAt,
            CompletedAt = attempt.CompletedAt,
            HttpStatusCode = attempt.HttpStatusCode,
            FailureCategory = attempt.FailureCategory,
            DurationMs = attempt.DurationMs,
            NextRetryAt = attempt.NextRetryAt,
            CreatedAt = attempt.CreatedAt,
            UpdatedAt = attempt.UpdatedAt
        };
    }
}
