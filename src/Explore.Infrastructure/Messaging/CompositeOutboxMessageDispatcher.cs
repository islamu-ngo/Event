using System.Text.Json;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Events.Handlers.Commands;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.Management.Handlers.Commands;
using Explore.Application.Features.Management.Requests.Commands;
using Explore.Application.Models.InternalEvents;
using Explore.Application.Services;
using Explore.Application.Services.Registration;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services.Moderation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Messaging;

public sealed class CompositeOutboxMessageDispatcher(
    NotificationFanoutOccurrenceHandoffService notificationFanoutOccurrenceHandoffService,
    IEventPublishedNotificationFanoutService notificationFanoutService,
    IEventModerationNotificationFanoutService moderationNotificationFanoutService,
    IReportProviderSyncDispatcher reportProviderSyncDispatcher,
    LocationPrivacyCorrectionDispatcher locationPrivacyCorrectionDispatcher,
    PrivacyErasureCacheInvalidationDispatcher privacyErasureCacheInvalidationDispatcher,
    IAdmissionCredentialDeliveryOutboxHandler admissionCredentialDeliveryHandler,
    IAdmissionRecoveryRequestOutboxHandler admissionRecoveryRequestHandler,
    IAdmissionRecoveryDeliveryOutboxHandler admissionRecoveryDeliveryHandler,
    IOutboxRepository outboxRepository,
    IRefundCampaignRepository refundCampaignRepository,
    RefundCampaignProcessor refundCampaignProcessor,
    RefundDispatchService refundDispatchService,
    RefundReconciliationService refundReconciliationService,
    RegistrationPaymentCancellationService paymentCancellationService,
    IAdmissionRevocationService admissionRevocationService,
    IAdmissionRefundRevocationService admissionRefundRevocationService,
    IAdmissionEventCancellationService admissionEventCancellationService,
    BusinessMetrics businessMetrics,
    TimeProvider timeProvider,
    IMediator mediator,
    ILogger<CompositeOutboxMessageDispatcher> logger,
    IConfigurationManifestEffectDispatcher configurationManifestEffectDispatcher,
    IConfigurationImportEffectDelivery configurationImportEffectDelivery) : IOutboxMessageDispatcher
{
    public async Task DispatchAsync(OutboxMessage message, CancellationToken ct = default)
    {
        switch (message.EventType)
        {
            case NotificationFanoutOccurrenceOutboxMessageFactory.EventType:
                await notificationFanoutOccurrenceHandoffService.HandoffAsync(message, ct);
                return;

            case PublishEventCommandHandler.EventPublishedNotificationFanoutRequestedEventType:
                await DispatchNotificationFanoutAsync(message, ct);
                return;

            case EventModerationOutboxMessageFactory.EventLightModeratedNotificationFanoutRequestedEventType:
                await DispatchLightModerationNotificationFanoutAsync(message, ct);
                return;

            case EventModerationOutboxMessageFactory.EventHeavyRedactedNotificationFanoutRequestedEventType:
                await DispatchHeavyRedactionNotificationFanoutAsync(message, ct);
                return;

            case EventReportOutboxMessageFactory.EventReportProviderSyncRequestedEventType:
                await reportProviderSyncDispatcher.DispatchAsync(message, ct);
                return;

            case LocationPrivacyOutboxMessageFactory.LocationPiiErasedEventType:
            case LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType:
            case LocationPrivacyCorrectionDispatcher.GovernanceCorrectionEventType:
                await locationPrivacyCorrectionDispatcher.DispatchAsync(message, ct);
                return;

            case PrivacyErasureCacheInvalidationOutboxMessageFactory.EventType:
                await privacyErasureCacheInvalidationDispatcher.DispatchAsync(message, ct);
                return;

            case ConfigurationManifestEffectOutbox.EventType:
                await configurationManifestEffectDispatcher.DispatchAsync(
                    message.AggregateId,
                    ct);
                return;

            case ConfigurationImportEffectOutbox.EventType:
                await configurationImportEffectDelivery.DispatchClaimedAsync(
                    message,
                    ct);
                return;

            case RegistrationOrderOutboxMessageFactory.RejectedEventType:
            case RegistrationOrderOutboxMessageFactory.ConfirmedEventType:
                logger.LogInformation("Recorded registration-order lifecycle outbox message {MessageId} after commit.", message.Id);
                return;

            case RegistrationOrderOutboxMessageFactory.CancelledEventType:
                RegistrationOrderLifecycleOutboxPayload cancelled =
                    RegistrationOrderOutboxMessageFactory.ReadLifecycle(message);
                AdmissionRevocationResult cancellationResult =
                    await admissionRevocationService.ReconcileAsync(
                        new AdmissionRevocationRequest(
                            cancelled.TenantId,
                            cancelled.RegistrationOrderId,
                            AdmissionRevocationService.OrderCancellationReason,
                            []),
                        ct);
                if (cancellationResult.Outcome is
                    AdmissionRevocationOutcome.InvalidRequest or
                    AdmissionRevocationOutcome.InvalidAllocation)
                {
                    throw new InvalidOperationException("Durable order cancellation admission facts are invalid.");
                }
                return;

            case AdmissionRevocationOutboxMessageFactory.EventCancellationRequested:
                AdmissionEventCancellationPayload eventCancellation =
                    AdmissionRevocationOutboxMessageFactory.ReadEventCancellation(message);
                await admissionEventCancellationService.ReconcileAsync(
                    message.Id,
                    eventCancellation.TenantId,
                    eventCancellation.EventId,
                    ct);
                return;

            case AdmissionDeliveryEvents.CredentialDeliveryRequested:
                await admissionCredentialDeliveryHandler.HandleAsync(message, ct);
                return;

            case AdmissionRecoveryDeliveryEvents.RecoveryRequestProcessingRequested:
                await admissionRecoveryRequestHandler.HandleAsync(message, ct);
                return;

            case AdmissionRecoveryDeliveryEvents.RecoveryDeliveryRequested:
                await admissionRecoveryDeliveryHandler.HandleAsync(message, ct);
                return;

            case RefundOutboxMessageFactory.CampaignProcessRequested:
                RefundCampaignProcessPayload campaign = RefundOutboxMessageFactory.ReadCampaign(message);
                RefundCampaign? processed = await refundCampaignProcessor.ProcessBatchAsync(
                    campaign.TenantId, campaign.CampaignId, message.Id, ct);
                businessMetrics.RecordRefundCampaignOperation(
                    processed?.Kind.ToString(), processed?.Status.ToString(), "completed");
                return;

            case RefundOutboxMessageFactory.DispatchRequested:
                await DispatchRefundAsync(message, ct);
                return;

            case RefundOutboxMessageFactory.ReconciliationRequested:
                await ReconcileRefundAsync(message, ct);
                return;

            case RefundOutboxMessageFactory.PaymentCancellationRequested:
                PaymentCancellationProcessPayload cancellation = RefundOutboxMessageFactory.ReadPaymentCancellation(message);
                if (!await paymentCancellationService.CancelAsync(
                        cancellation.TenantId, cancellation.CampaignId, cancellation.PaymentAttemptId, ct))
                {
                    throw new InvalidOperationException("Provider payment cancellation remains ambiguous and will be retried idempotently.");
                }
                return;

            case ManagedTenantProvisioningOutboxEvents.ProcessRequested:
                await mediator.Send(
                    new ProcessManagedTenantProvisioningOperationCommand(message.AggregateId, message.Id),
                    ct);
                return;

            default:
                throw new InvalidOperationException(
                    $"Unknown outbox EventType '{message.EventType}' for message {message.Id}. " +
                    $"Add a route in {nameof(CompositeOutboxMessageDispatcher)}.");
        }
    }

    private async Task DispatchRefundAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        RefundAttemptProcessPayload payload = RefundOutboxMessageFactory.ReadAttempt(message);
        RefundAttempt? attempt = await refundDispatchService.DispatchAsync(
            payload.TenantId, payload.RefundAttemptId, cancellationToken);
        if (attempt is null)
        {
            return;
        }

        if (attempt.Status == RefundAttemptStatusEnum.DispatchPending)
        {
            businessMetrics.RecordRefundOperation("dispatch", attempt.Status.ToString(), "retry");
            throw new InvalidOperationException("Refund dispatch remains safely retryable before provider handoff.");
        }
        businessMetrics.RecordRefundOperation("dispatch", attempt.Status.ToString(), "completed");
        await ReconcileAdmissionRefundAsync(attempt, cancellationToken);
        await ScheduleReconciliationAsync(attempt, cancellationToken);
        if (attempt.Status == RefundAttemptStatusEnum.RequiresAction && attempt.SourceCampaignId.HasValue)
        {
            await refundCampaignRepository.RequireOperatorAsync(
                attempt.TenantId,
                attempt.SourceCampaignId.Value,
                timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken);
        }
        await RefreshCampaignAsync(attempt, cancellationToken);
    }

    private async Task ReconcileRefundAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        RefundAttemptProcessPayload payload = RefundOutboxMessageFactory.ReadAttempt(message);
        RefundAttempt? attempt = await refundReconciliationService.ReconcileAsync(
            payload.TenantId, payload.RefundAttemptId, cancellationToken);
        if (attempt is null)
        {
            return;
        }

        businessMetrics.RecordRefundOperation("reconcile", attempt.Status.ToString(), "completed");
        await ReconcileAdmissionRefundAsync(attempt, cancellationToken);
        await ScheduleReconciliationAsync(attempt, cancellationToken);
        await RefreshCampaignAsync(attempt, cancellationToken);
    }

    private Task ScheduleReconciliationAsync(RefundAttempt attempt, CancellationToken cancellationToken)
    {
        if (attempt.Status is not (RefundAttemptStatusEnum.Pending or RefundAttemptStatusEnum.RequiresAction or RefundAttemptStatusEnum.Unknown) ||
            attempt.Status == RefundAttemptStatusEnum.RequiresAction && attempt.FailureCode is not null)
        {
            return Task.CompletedTask;
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        return outboxRepository.Create(
            RefundOutboxMessageFactory.CreateReconciliation(attempt, now, now.AddMinutes(1)));
    }

    private Task RefreshCampaignAsync(RefundAttempt attempt, CancellationToken cancellationToken) =>
        attempt.SourceCampaignId.HasValue
            ? refundCampaignRepository.RefreshOutcomeCountersAsync(
                attempt.TenantId, attempt.SourceCampaignId.Value, timeProvider.GetUtcNow().UtcDateTime, cancellationToken)
            : Task.CompletedTask;

    private async Task ReconcileAdmissionRefundAsync(
        RefundAttempt attempt,
        CancellationToken cancellationToken)
    {
        if (!attempt.BuyerRefundSucceededAt.HasValue)
        {
            return;
        }

        AdmissionRevocationResult? result =
            await admissionRefundRevocationService.ReconcileSucceededAsync(
                attempt.TenantId,
                attempt.Id,
                cancellationToken);
        if (result?.Outcome is
            AdmissionRevocationOutcome.InvalidRequest or
            AdmissionRevocationOutcome.InvalidAllocation or
            AdmissionRevocationOutcome.NotFound)
        {
            throw new InvalidOperationException("Refund admission reconciliation did not converge.");
        }
    }

    public async Task ReconcileDeadLetterAsync(
        OutboxMessage message,
        CancellationToken ct = default)
    {
        switch (message.EventType)
        {
            case ConfigurationManifestEffectOutbox.EventType:
                await configurationManifestEffectDispatcher.DispatchAsync(
                    message.AggregateId,
                    ct);
                return;

            case ConfigurationImportEffectOutbox.EventType:
                await configurationImportEffectDelivery.DispatchClaimedAsync(
                    message,
                    ct);
                return;

            case ManagedTenantProvisioningOutboxEvents.ProcessRequested:
                await mediator.Send(
                    new ReconcileManagedTenantProvisioningDeadLetterCommand(message.AggregateId, message.Id),
                    ct);
                return;

            case LocationPrivacyOutboxMessageFactory.LocationPiiErasedEventType:
            case LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType:
            case LocationPrivacyCorrectionDispatcher.GovernanceCorrectionEventType:
                await locationPrivacyCorrectionDispatcher.ReconcileDeadLetterAsync(message, ct);
                return;

            case PrivacyErasureCacheInvalidationOutboxMessageFactory.EventType:
                await privacyErasureCacheInvalidationDispatcher.DispatchAsync(message, ct);
                return;

            case AdmissionDeliveryEvents.CredentialDeliveryRequested:
                await admissionCredentialDeliveryHandler.HandleAsync(message, ct);
                return;

            case AdmissionRecoveryDeliveryEvents.RecoveryRequestProcessingRequested:
                await admissionRecoveryRequestHandler.HandleAsync(message, ct);
                return;

            case AdmissionRecoveryDeliveryEvents.RecoveryDeliveryRequested:
                await admissionRecoveryDeliveryHandler.HandleAsync(message, ct);
                return;

            case RegistrationOrderOutboxMessageFactory.CancelledEventType:
            case AdmissionRevocationOutboxMessageFactory.EventCancellationRequested:
            case RefundOutboxMessageFactory.CampaignProcessRequested:
            case RefundOutboxMessageFactory.DispatchRequested:
            case RefundOutboxMessageFactory.ReconciliationRequested:
            case RefundOutboxMessageFactory.PaymentCancellationRequested:
                await DispatchAsync(message, ct);
                return;

            default:
                throw new InvalidOperationException(
                    $"Outbox EventType '{message.EventType}' for message {message.Id} " +
                    "does not support dead-letter reconciliation.");
        }
    }

    private async Task DispatchNotificationFanoutAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.Payload))
        {
            throw new InvalidOperationException($"Outbox message {message.Id} has no payload for notification fanout.");
        }

        var request = JsonSerializer.Deserialize<EventPublishedNotificationFanoutRequested>(message.Payload)
            ?? throw new JsonException($"Failed to deserialize notification fanout payload for message {message.Id}.");

        logger.LogInformation(
            "Dispatching internal notification fanout for event {EventId} from outbox message {MessageId}",
            request.EventId,
            message.Id);

        await notificationFanoutService.FanoutAsync(request, cancellationToken);
    }

    private async Task DispatchLightModerationNotificationFanoutAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.Payload))
        {
            throw new InvalidOperationException($"Outbox message {message.Id} has no payload for light moderation notification fanout.");
        }

        var request = JsonSerializer.Deserialize<EventLightModeratedNotificationFanoutRequested>(message.Payload)
            ?? throw new JsonException($"Failed to deserialize light moderation notification fanout payload for message {message.Id}.");

        logger.LogInformation(
            "Dispatching internal light moderation notification fanout for event {EventId} from outbox message {MessageId}",
            request.EventId,
            message.Id);

        await moderationNotificationFanoutService.FanoutLightModerationAsync(request, cancellationToken);
    }

    private async Task DispatchHeavyRedactionNotificationFanoutAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.Payload))
        {
            throw new InvalidOperationException($"Outbox message {message.Id} has no payload for heavy redaction notification fanout.");
        }

        EventHeavyRedactedNotificationFanoutPayloadParseResult parsed;
        try
        {
            parsed = EventHeavyRedactedNotificationFanoutPayloadParser.Parse(message.Payload);
        }
        catch (JsonException)
        {
            await outboxRepository.TryReplaceProcessingPayloadAsync(
                message.Id,
                message.Payload,
                EventHeavyRedactedNotificationFanoutPayloadParser.SafeInvalidPayload,
                cancellationToken);
            throw new JsonException("The heavy moderation fanout payload is invalid.");
        }

        if (parsed.WasLegacy)
        {
            bool scrubbed = await outboxRepository.TryReplaceProcessingPayloadAsync(
                message.Id,
                message.Payload,
                parsed.CanonicalPayload,
                cancellationToken);
            if (!scrubbed)
            {
                throw new InvalidOperationException("The retained heavy moderation fanout payload could not be safely replaced.");
            }

            message.Payload = parsed.CanonicalPayload;
        }

        logger.LogInformation(
            "Dispatching internal heavy redaction notification fanout for moderation record {ModerationRecordId} from outbox message {MessageId}",
            parsed.Request.ModerationRecordId,
            message.Id);

        await moderationNotificationFanoutService.FanoutHeavyRedactionAsync(parsed.Request, cancellationToken);
    }
}
