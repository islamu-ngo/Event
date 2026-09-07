using Explore.Application.DTOs.Webhooks;
using Explore.Application.Lookups;
using Explore.Domain;

namespace Explore.Application.Features.Webhooks.Handlers;

internal static class WebhookBulkReplayDtoMapper
{
    public static WebhookBulkReplayOperationDto Map(WebhookBulkReplayOperation operation)
    {
        var status = NormalizedLookupMetadata.WebhookBulkReplayStatus(operation.StatusId);
        return new WebhookBulkReplayOperationDto
        {
            Id = operation.Id,
            TenantId = operation.TenantId,
            OperationKey = operation.OperationKey,
            StatusId = status.Id,
            StatusCode = status.Code,
            StatusName = status.Name,
            Filter = new WebhookBulkReplayFilterDto
            {
                FromUtc = operation.FromUtc,
                ToUtc = operation.ToUtc,
                WebhookConsumerId = operation.WebhookConsumerId,
                WebhookEndpointId = operation.WebhookEndpointId,
                EventType = operation.EventType,
                MaxItems = operation.RequestedMaxItems
            },
            ReasonCode = operation.ReasonCode,
            CancellationReasonCode = operation.CancellationReasonCode,
            EstimatedEligibleCount = operation.EstimatedEligibleCount,
            EstimatedSelectedCount = operation.EstimatedSelectedCount,
            EstimatedExcludedCount = operation.EstimatedExcludedCount,
            ScheduledCount = operation.ScheduledCount,
            FailureCode = operation.FailureCode,
            ConcurrencyVersion = operation.ConcurrencyVersion,
            QueuedAt = operation.QueuedAt,
            StartedAt = operation.StartedAt,
            CompletedAt = operation.CompletedAt,
            CancelledAt = operation.CancelledAt,
            FailedAt = operation.FailedAt
        };
    }
}
