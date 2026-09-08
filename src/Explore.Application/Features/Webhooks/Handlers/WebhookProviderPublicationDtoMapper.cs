using Explore.Application.DTOs.Webhooks;
using Explore.Application.Lookups;
using Explore.Domain;

namespace Explore.Application.Features.Webhooks.Handlers;

internal static class WebhookProviderPublicationDtoMapper
{
    public static WebhookProviderPublicationDto Map(WebhookProviderPublication publication)
    {
        var providerKind = NormalizedLookupMetadata.WebhookProviderKind(publication.ProviderKindId);
        var mode = NormalizedLookupMetadata.WebhookProviderMode(publication.ModeSnapshotId);
        var status = NormalizedLookupMetadata.WebhookProviderPublicationStatus(publication.StatusId);

        return new WebhookProviderPublicationDto
        {
            Id = publication.Id,
            TenantId = publication.TenantId,
            WebhookMessageId = publication.WebhookMessageId,
            WebhookConsumerId = publication.WebhookDeliveryPlanSnapshot?.WebhookConsumerId ?? Guid.Empty,
            WebhookDeliveryPlanSnapshotId = publication.WebhookDeliveryPlanSnapshotId,
            ProviderKindId = providerKind.Id,
            ProviderKindCode = providerKind.Code,
            ProviderKindName = providerKind.Name,
            ModeSnapshotId = mode.Id,
            ModeSnapshotCode = mode.Code,
            ModeSnapshotName = mode.Name,
            StatusId = status.Id,
            StatusCode = status.Code,
            StatusName = status.Name,
            ProviderVersion = publication.ProviderVersion,
            ProviderEventId = publication.ProviderEventId,
            RequestHash = publication.RequestHash,
            ProviderEnvironment = publication.ProviderEnvironment,
            ProviderApplicationId = publication.ProviderApplicationId,
            ExternalProviderMessageId = publication.ExternalProviderMessageId,
            AutomaticPublicationAttemptCount = publication.AutomaticPublicationAttemptCount,
            AutomaticReconciliationAttemptCount = publication.AutomaticReconciliationAttemptCount,
            LastAutomaticReconciliationAt = publication.LastAutomaticReconciliationAt,
            NextActionAt = publication.NextActionAt,
            FailureCategory = publication.FailureCategory,
            SafeDetail = publication.SafeDetail,
            PublicationFence = publication.PublicationFence,
            ConcurrencyVersion = publication.ConcurrencyVersion,
            EventContractVersion = publication.EventContractVersion,
            ProviderConfigurationVersion = publication.ProviderConfigurationVersion,
            RetentionPolicyVersion = publication.RetentionPolicyVersion,
            PayloadRetentionUntil = publication.PayloadRetentionUntil,
            PublicationRetentionUntil = publication.PublicationRetentionUntil,
            IdempotencyValidUntil = publication.IdempotencyValidUntil,
            PreparedAt = publication.PreparedAt,
            PublishingStartedAt = publication.PublishingStartedAt,
            ProviderQueuedAt = publication.ProviderQueuedAt,
            PublicationUnknownAt = publication.PublicationUnknownAt,
            DeadLetteredAt = publication.DeadLetteredAt,
            ManualReconciliationAt = publication.ManualReconciliationAt,
            AbandonedAt = publication.AbandonedAt,
            ProcessingLeaseExpiresAt = publication.ProcessingLeaseExpiresAt,
            CreatedAt = publication.CreatedAt,
            UpdatedAt = publication.UpdatedAt,
            Attempts = publication.Attempts
                .OrderBy(attempt => attempt.AttemptNumber)
                .Select(MapAttempt)
                .ToArray()
        };
    }

    private static WebhookProviderPublicationAttemptDto MapAttempt(WebhookProviderPublicationAttempt attempt)
    {
        var outcome = NormalizedLookupMetadata.WebhookProviderPublicationAttemptOutcome(attempt.OutcomeId);
        return new WebhookProviderPublicationAttemptDto
        {
            Id = attempt.Id,
            AttemptNumber = attempt.AttemptNumber,
            PublicationFence = attempt.PublicationFence,
            OutcomeId = outcome.Id,
            OutcomeCode = outcome.Code,
            OutcomeName = outcome.Name,
            StartedAt = attempt.StartedAt,
            RecordedAt = attempt.RecordedAt,
            ExternalProviderMessageId = attempt.ExternalProviderMessageId,
            FailureCategory = attempt.FailureCategory,
            SafeDetail = attempt.SafeDetail
        };
    }
}
