using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Telemetry;
using Explore.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Webhooks;

public sealed class WebhookProviderPublicationDrainService(
    IWebhookProviderPublicationRepository publicationRepository,
    WebhookProviderPublicationDispatcher dispatcher,
    WebhookProviderPublicationReconciler reconciler,
    IOptions<WebhookProviderPublicationProcessorSettings> settings,
    TimeProvider timeProvider,
    BusinessMetrics metrics,
    ILogger<WebhookProviderPublicationDrainService> logger)
    : IWebhookProviderPublicationDrainService
{
    private readonly WebhookProviderPublicationProcessorSettings _settings = settings.Value;

    public async Task<WebhookProviderPublicationDrainResult> ProcessBatchAsync(
        CancellationToken cancellationToken)
    {
        var claimedAt = timeProvider.GetUtcNow().UtcDateTime;
        var claims = await publicationRepository.ClaimDueAsync(
            new WebhookProviderPublicationClaimRequest(
                _settings.BatchSize,
                CreateLeaseOwner(),
                claimedAt,
                TimeSpan.FromSeconds(_settings.LeaseSeconds),
                _settings.MaxAutomaticPublicationAttempts),
            cancellationToken);

        var providerQueued = 0;
        var retryScheduled = 0;
        var publicationUnknown = 0;
        var deadLettered = 0;
        var leaseLost = 0;
        var failed = 0;

        foreach (var claim in claims)
        {
            metrics.RecordWebhookClaimLag(
                WebhookTelemetryProvider.Svix,
                WebhookTelemetryOperation.Publication,
                claimedAt - claim.DueAt);
        }
        metrics.RecordWebhookProcessingOutcome(
            WebhookTelemetryProvider.Svix,
            WebhookTelemetryOperation.Publication,
            WebhookTelemetryOutcome.Claimed,
            claims.Count);

        foreach (var claim in claims)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await dispatcher.DispatchAsync(claim, cancellationToken);
                RecordPublicationOutcome(result.Outcome);
                switch (result.Outcome)
                {
                    case WebhookProviderPublicationDispatchOutcome.ProviderQueued:
                        providerQueued++;
                        break;
                    case WebhookProviderPublicationDispatchOutcome.RetryScheduled:
                        retryScheduled++;
                        break;
                    case WebhookProviderPublicationDispatchOutcome.PublicationUnknown:
                        publicationUnknown++;
                        break;
                    case WebhookProviderPublicationDispatchOutcome.DeadLettered:
                        deadLettered++;
                        break;
                    case WebhookProviderPublicationDispatchOutcome.LeaseLost:
                        leaseLost++;
                        break;
                    default:
                        failed++;
                        break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failed++;
                metrics.RecordWebhookProcessingOutcome(
                    WebhookTelemetryProvider.Svix,
                    WebhookTelemetryOperation.Publication,
                    WebhookTelemetryOutcome.Failed);
                logger.LogError(
                    "Provider publication dispatch failed. FailureType={FailureType}",
                    exception.GetType().Name);
            }
        }

        return new WebhookProviderPublicationDrainResult(
            claims.Count,
            providerQueued,
            retryScheduled,
            publicationUnknown,
            deadLettered,
            leaseLost,
            failed);
    }

    public async Task<WebhookProviderReconciliationDrainResult> ProcessReconciliationBatchAsync(
        CancellationToken cancellationToken)
    {
        var observedAt = timeProvider.GetUtcNow().UtcDateTime;
        var manualCandidates = await publicationRepository.GetUnknownRequiringManualAsync(
            observedAt,
            _settings.BatchSize,
            _settings.MaxAutomaticReconciliationAttempts,
            cancellationToken);
        var manualReconciliation = 0;
        var leaseLost = 0;
        var failed = 0;

        foreach (var publication in manualCandidates)
        {
            metrics.RecordWebhookPublicationUnknownAge(
                WebhookTelemetryProvider.Svix,
                observedAt - (publication.PublicationUnknownAt ?? observedAt));
            try
            {
                var result = await reconciler.ReconcileExpiredOrExhaustedAsync(
                    publication,
                    cancellationToken);
                RecordReconciliationOutcome(result.Outcome);
                if (result.Outcome == WebhookProviderReconciliationOutcome.ManualReconciliation)
                {
                    manualReconciliation++;
                }
                else if (result.Outcome == WebhookProviderReconciliationOutcome.LeaseLost)
                {
                    leaseLost++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failed++;
                metrics.RecordWebhookProcessingOutcome(
                    WebhookTelemetryProvider.Svix,
                    WebhookTelemetryOperation.Reconciliation,
                    WebhookTelemetryOutcome.Failed);
                logger.LogError(
                    "Provider publication terminal reconciliation failed. FailureType={FailureType}",
                    exception.GetType().Name);
            }
        }

        var claimedAt = timeProvider.GetUtcNow().UtcDateTime;
        var claims = await publicationRepository.ClaimUnknownAsync(
            new WebhookProviderPublicationClaimRequest(
                _settings.BatchSize,
                CreateLeaseOwner(),
                claimedAt,
                TimeSpan.FromSeconds(_settings.LeaseSeconds),
                _settings.MaxAutomaticReconciliationAttempts),
            cancellationToken);
        var providerQueued = 0;
        var retryScheduled = 0;
        var deferred = 0;

        foreach (var claim in claims)
        {
            metrics.RecordWebhookClaimLag(
                WebhookTelemetryProvider.Svix,
                WebhookTelemetryOperation.Reconciliation,
                claimedAt - claim.DueAt);
            metrics.RecordWebhookPublicationUnknownAge(
                WebhookTelemetryProvider.Svix,
                claimedAt - (claim.Publication.PublicationUnknownAt ?? claimedAt));
        }
        metrics.RecordWebhookProcessingOutcome(
            WebhookTelemetryProvider.Svix,
            WebhookTelemetryOperation.Reconciliation,
            WebhookTelemetryOutcome.Claimed,
            claims.Count);

        foreach (var claim in claims)
        {
            try
            {
                var result = await reconciler.ReconcileAsync(claim, cancellationToken);
                RecordReconciliationOutcome(result.Outcome);
                switch (result.Outcome)
                {
                    case WebhookProviderReconciliationOutcome.ProviderQueued:
                        providerQueued++;
                        break;
                    case WebhookProviderReconciliationOutcome.RetryScheduled:
                        retryScheduled++;
                        break;
                    case WebhookProviderReconciliationOutcome.Deferred:
                        deferred++;
                        break;
                    case WebhookProviderReconciliationOutcome.ManualReconciliation:
                        manualReconciliation++;
                        break;
                    case WebhookProviderReconciliationOutcome.LeaseLost:
                        leaseLost++;
                        break;
                    default:
                        failed++;
                        break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failed++;
                metrics.RecordWebhookProcessingOutcome(
                    WebhookTelemetryProvider.Svix,
                    WebhookTelemetryOperation.Reconciliation,
                    WebhookTelemetryOutcome.Failed);
                logger.LogError(
                    "Provider publication reconciliation failed. FailureType={FailureType}",
                    exception.GetType().Name);
            }
        }

        return new WebhookProviderReconciliationDrainResult(
            manualCandidates.Count,
            claims.Count,
            providerQueued,
            retryScheduled,
            deferred,
            manualReconciliation,
            leaseLost,
            failed);
    }

    private void RecordPublicationOutcome(WebhookProviderPublicationDispatchOutcome outcome)
    {
        var telemetryOutcome = outcome switch
        {
            WebhookProviderPublicationDispatchOutcome.ProviderQueued => WebhookTelemetryOutcome.ProviderQueued,
            WebhookProviderPublicationDispatchOutcome.RetryScheduled => WebhookTelemetryOutcome.RetryScheduled,
            WebhookProviderPublicationDispatchOutcome.PublicationUnknown => WebhookTelemetryOutcome.PublicationUnknown,
            WebhookProviderPublicationDispatchOutcome.DeadLettered => WebhookTelemetryOutcome.DeadLettered,
            WebhookProviderPublicationDispatchOutcome.LeaseLost => WebhookTelemetryOutcome.LeaseLost,
            _ => WebhookTelemetryOutcome.Failed
        };
        metrics.RecordWebhookProcessingOutcome(
            WebhookTelemetryProvider.Svix,
            WebhookTelemetryOperation.Publication,
            telemetryOutcome);

        if (outcome == WebhookProviderPublicationDispatchOutcome.RetryScheduled)
        {
            metrics.RecordWebhookRetryScheduled(
                WebhookTelemetryProvider.Svix,
                WebhookTelemetryOperation.Publication);
        }
        else if (outcome == WebhookProviderPublicationDispatchOutcome.DeadLettered)
        {
            metrics.RecordWebhookDeadLetter(
                WebhookTelemetryProvider.Svix,
                WebhookTelemetryOperation.Publication);
        }
    }

    private void RecordReconciliationOutcome(WebhookProviderReconciliationOutcome outcome)
    {
        var telemetryOutcome = outcome switch
        {
            WebhookProviderReconciliationOutcome.ProviderQueued => WebhookTelemetryOutcome.ProviderQueued,
            WebhookProviderReconciliationOutcome.RetryScheduled => WebhookTelemetryOutcome.RetryScheduled,
            WebhookProviderReconciliationOutcome.Deferred => WebhookTelemetryOutcome.Deferred,
            WebhookProviderReconciliationOutcome.ManualReconciliation => WebhookTelemetryOutcome.ManualReconciliation,
            WebhookProviderReconciliationOutcome.LeaseLost => WebhookTelemetryOutcome.LeaseLost,
            _ => WebhookTelemetryOutcome.Failed
        };
        metrics.RecordWebhookProcessingOutcome(
            WebhookTelemetryProvider.Svix,
            WebhookTelemetryOperation.Reconciliation,
            telemetryOutcome);

        if (outcome == WebhookProviderReconciliationOutcome.RetryScheduled)
        {
            metrics.RecordWebhookRetryScheduled(
                WebhookTelemetryProvider.Svix,
                WebhookTelemetryOperation.Reconciliation);
        }
        else if (outcome == WebhookProviderReconciliationOutcome.ManualReconciliation)
        {
            metrics.RecordWebhookManualReconciliation(WebhookTelemetryProvider.Svix);
        }
    }

    private static string CreateLeaseOwner()
    {
        var owner = $"provider-publication:{Environment.MachineName}:{Environment.ProcessId}";
        return owner.Length <= WebhookProviderPublication.MaxLeaseOwnerLength
            ? owner
            : owner[..WebhookProviderPublication.MaxLeaseOwnerLength];
    }
}
