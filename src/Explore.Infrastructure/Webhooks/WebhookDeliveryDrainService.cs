using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Webhooks;

public sealed class WebhookDeliveryDrainService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IWebhookSignatureService signatureService,
    WebhookEndpointSafetyPolicy safetyPolicy,
    WebhookRetryScheduler retryScheduler,
    WebhookEndpointSecretResolver secretResolver,
    IOptions<WebhookDeliveryProcessorSettings> settings,
    IOptionsMonitor<WebhookOptions> webhookOptions,
    BusinessMetrics metrics,
    ILogger<WebhookDeliveryDrainService> logger) : IWebhookDeliveryDrainService
{
    public const string HttpClientName = "LocalWebhookDeliveryClient";

    private const string ProcessingLeaseExpiredFailureCategory = "processing_lease_expired";
    private const string MissingMessageFailureCategory = "missing_message";
    private const string EndpointNotActiveFailureCategory = "endpoint_not_active";
    private const string PayloadUnavailableFailureCategory = "payload_unavailable";
    private const string PayloadTooLargeFailureCategory = "payload_too_large";
    private const string MissingSecretFailureCategory = "missing_secret";
    private const string InvalidSecretFailureCategory = "invalid_secret";
    private const string InvalidUrlFailureCategory = "invalid_url";
    private const string RedirectResponseFailureCategory = "redirect_response";
    private const string HttpNonSuccessFailureCategory = "http_non_success";
    private const string TimeoutFailureCategory = "timeout";
    private const string NetworkFailureCategory = "network_error";
    private const string ProviderDisabledFailureCategory = "provider_disabled";
    private const string AttemptStatusNotRetryableFailureCategory = "attempt_status_not_retryable";
    private const string MessagePayloadRetentionExpiredFailureCategory = "message_payload_retention_expired";

    private readonly WebhookDeliveryProcessorSettings _settings = settings.Value;
    public async Task<WebhookDeliveryDrainResult> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        if (!ShouldProcessLocalTargets())
        {
            return new WebhookDeliveryDrainResult(0, 0, 0, 0, 0, 0, 0);
        }

        var claimedAt = DateTimeOffset.UtcNow;
        IReadOnlyList<WebhookLocalTargetClaim> claims;
        int candidateCount;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var targetRepository = scope.ServiceProvider.GetRequiredService<IWebhookLocalTargetRepository>();
            var governanceResolver = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryGovernanceResolver>();
            var instancePolicy = await governanceResolver.ResolveAsync(null, cancellationToken);
            var tenantOrder = await targetRepository.GetDueTenantIdsAsync(
                _settings.CandidateBatchSize,
                claimedAt,
                cancellationToken);
            candidateCount = await targetRepository.CountDueAsync(claimedAt, cancellationToken);
            if (tenantOrder.Count == 0)
            {
                claims = [];
            }
            else
            {
                var limits = new Dictionary<Guid, WebhookDeliveryClaimLimits>(tenantOrder.Count);
                foreach (var tenantId in tenantOrder)
                {
                    var tenantPolicy = await governanceResolver.ResolveAsync(tenantId, cancellationToken);
                    limits[tenantId] = new WebhookDeliveryClaimLimits(
                        tenantPolicy.MaxInFlightPerTenant,
                        tenantPolicy.MaxInFlightPerEndpoint,
                        tenantPolicy.MaxItemsPerTenantPerClaimCycle);
                }

                claims = await targetRepository.ClaimDueAsync(
                    new WebhookLocalTargetClaimRequest(
                        _settings.BatchSize,
                        _settings.CandidateBatchSize,
                        instancePolicy.GlobalInFlightLimit,
                        tenantOrder,
                        claimedAt,
                        TimeSpan.FromSeconds(_settings.ProcessingLeaseTimeoutSeconds)),
                    limits,
                    cancellationToken);
            }
        }

        foreach (var claim in claims)
        {
            metrics.RecordWebhookClaimLag(
                WebhookTelemetryProvider.Local,
                WebhookTelemetryOperation.Delivery,
                claimedAt - claim.Target.NextActionAtUtc);
        }
        metrics.RecordWebhookProcessingOutcome(
            WebhookTelemetryProvider.Local,
            WebhookTelemetryOperation.Delivery,
            WebhookTelemetryOutcome.Claimed,
            claims.Count);

        if (claims.Count == 0)
        {
            if (_settings.VerboseLogging)
            {
                logger.LogDebug("No due webhook delivery attempts");
            }

            return new WebhookDeliveryDrainResult(0, 0, 0, 0, 0, 0, 0);
        }

        var results = await Task.WhenAll(claims.Select(claim =>
            SendClaimedTargetAsync(claim, cancellationToken)));
        var succeeded = results.Count(result => result.Outcome == WebhookDeliveryDrainOutcome.Succeeded);
        var retryScheduled = results.Count(result => result.Outcome == WebhookDeliveryDrainOutcome.RetryScheduled);
        var abandoned = results.Count(result => result.Outcome == WebhookDeliveryDrainOutcome.Abandoned);
        var alreadyClaimed = results.Count(result => result.Outcome == WebhookDeliveryDrainOutcome.AlreadyClaimed);
        var processed = succeeded + retryScheduled + abandoned;
        var skipped = results.Length - processed - alreadyClaimed;

        return new WebhookDeliveryDrainResult(
            candidateCount,
            processed,
            succeeded,
            retryScheduled,
            abandoned,
            skipped,
            alreadyClaimed);
    }

    public async Task<WebhookDeliveryRecoveryResult> RecoverStaleProcessingAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var targetRepository = scope.ServiceProvider.GetRequiredService<IWebhookLocalTargetRepository>();
        var recoveredAt = DateTimeOffset.UtcNow;
        var recovered = await targetRepository.RecoverExpiredClaimsAsync(
            recoveredAt,
            ProcessingLeaseExpiredFailureCategory,
            _settings.BatchSize,
            cancellationToken);

        metrics.RecordWebhookProcessingOutcome(
            WebhookTelemetryProvider.Local,
            WebhookTelemetryOperation.Recovery,
            WebhookTelemetryOutcome.Recovered,
            recovered);

        if (recovered > 0)
        {
            logger.LogWarning(
                "Recovered {RecoveredCount} stale webhook Local target claims. RecoveryCutoffUtc={RecoveryCutoffUtc:o}",
                recovered,
                recoveredAt);
        }
        else if (_settings.VerboseLogging)
        {
            logger.LogDebug(
                "No expired webhook Local target claims found at {RecoveryCutoffUtc:o}",
                recoveredAt);
        }

        return new WebhookDeliveryRecoveryResult(recovered, recoveredAt);
    }

    public async Task<WebhookDeliverySingleDrainResult> ScheduleManualRetryAsync(
        Guid tenantId,
        Guid attemptId,
        WebhookAuditPrincipalKind principalKind,
        string principalReference,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var attemptRepository = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryAttemptRepository>();
        var attempt = await attemptRepository.GetByTenantAndIdAsync(tenantId, attemptId, cancellationToken);
        if (attempt is null)
        {
            metrics.RecordWebhookManualRetry(null, "missing", MissingMessageFailureCategory);
            return new WebhookDeliverySingleDrainResult(WebhookDeliveryDrainOutcome.Missing);
        }

        if (!ShouldProcessLocalTargets())
        {
            RecordManualRetry(attempt, "skipped", ProviderDisabledFailureCategory);
            return new WebhookDeliverySingleDrainResult(WebhookDeliveryDrainOutcome.Skipped, attempt.Id);
        }

        if (attempt.Outcome == WebhookDeliveryAttemptOutcome.Succeeded)
        {
            RecordManualRetry(attempt, "already_settled", AttemptStatusNotRetryableFailureCategory);
            return new WebhookDeliverySingleDrainResult(WebhookDeliveryDrainOutcome.AlreadySettled, attempt.Id);
        }

        if (attempt.Outcome is WebhookDeliveryAttemptOutcome.Scheduled or WebhookDeliveryAttemptOutcome.Sending)
        {
            RecordManualRetry(attempt, "deferred", AttemptStatusNotRetryableFailureCategory);
            return new WebhookDeliverySingleDrainResult(WebhookDeliveryDrainOutcome.Deferred, attempt.Id);
        }

        if (attempt.Endpoint is null || attempt.Message is null)
        {
            RecordManualRetry(attempt, "missing", MissingMessageFailureCategory);
            return new WebhookDeliverySingleDrainResult(WebhookDeliveryDrainOutcome.Missing, attempt.Id);
        }

        if (attempt.Endpoint.Status != WebhookEndpointStatus.Active)
        {
            RecordManualRetry(attempt, "skipped", EndpointNotActiveFailureCategory);
            return new WebhookDeliverySingleDrainResult(WebhookDeliveryDrainOutcome.Skipped, attempt.Id);
        }

        var retryRequestedAt = DateTimeOffset.UtcNow;
        if (attempt.Message.PayloadRetentionUntil <= retryRequestedAt.UtcDateTime ||
            attempt.Message.GetPayloadBytes() is null)
        {
            RecordManualRetry(attempt, "skipped", MessagePayloadRetentionExpiredFailureCategory);
            return new WebhookDeliverySingleDrainResult(WebhookDeliveryDrainOutcome.Skipped, attempt.Id);
        }

        var targetRepository = scope.ServiceProvider.GetRequiredService<IWebhookLocalTargetRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var auditWriter = scope.ServiceProvider.GetRequiredService<IWebhookAuditEventWriter>();
        var result = await unitOfWork.ExecuteInTransactionAsync(async token =>
    {
        var target = await targetRepository.GetByMessageAndEndpointForUpdateAsync(
            tenantId,
            attempt.MessageId,
            attempt.EndpointId,
            token);
        if (target is null)
        {
            return new WebhookDeliverySingleDrainResult(WebhookDeliveryDrainOutcome.Missing, attempt.Id);
        }

        if (target.DeliveryStatus == WebhookLocalDeliveryStatus.Delivering)
        {
            return new WebhookDeliverySingleDrainResult(WebhookDeliveryDrainOutcome.AlreadyClaimed, attempt.Id);
        }

        if (target.DeliveryStatus is WebhookLocalDeliveryStatus.Pending or WebhookLocalDeliveryStatus.RetryDue)
        {
            return new WebhookDeliverySingleDrainResult(WebhookDeliveryDrainOutcome.Deferred, attempt.Id);
        }

        if (target.DeliveryStatus == WebhookLocalDeliveryStatus.Succeeded)
        {
            return new WebhookDeliverySingleDrainResult(WebhookDeliveryDrainOutcome.AlreadySettled, attempt.Id);
        }

        var previousTargetStatus = target.DeliveryStatus;
        target.ScheduleManualRetry(retryRequestedAt);
        await targetRepository.SaveChangesAsync(token);
        await auditWriter.AppendAsync(
            new WebhookAuditWriteRequest(
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
                PrincipalReference: principalReference),
            token);

        return new WebhookDeliverySingleDrainResult(WebhookDeliveryDrainOutcome.RetryScheduled, attempt.Id);
    }, cancellationToken);

        RecordManualRetry(
            attempt,
            result.Outcome == WebhookDeliveryDrainOutcome.RetryScheduled ? "retry_scheduled" : "skipped",
            result.Outcome == WebhookDeliveryDrainOutcome.RetryScheduled ? null : AttemptStatusNotRetryableFailureCategory);
        return result;
    }

    private async Task<WebhookDeliverySingleDrainResult> SendClaimedTargetAsync(
        WebhookLocalTargetClaim claim,
        CancellationToken cancellationToken)
    {
        await using var executionScope = scopeFactory.CreateAsyncScope();
        var tenantContextAccessor = executionScope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        tenantContextAccessor.SetTenant(claim.Target.TenantId);
        var targetRepository = executionScope.ServiceProvider.GetRequiredService<IWebhookLocalTargetRepository>();
        var activeTarget = await targetRepository.GetActiveClaimAsync(
            claim.Target.TenantId,
            claim.Target.Id,
            claim.LeaseToken,
            claim.DeliveryFence,
            DateTimeOffset.UtcNow,
            cancellationToken);
        if (activeTarget?.ProcessingLeaseExpiresAtUtc is not { } leaseExpiresAtUtc)
        {
            metrics.RecordWebhookProcessingOutcome(
                WebhookTelemetryProvider.Local,
                WebhookTelemetryOperation.Delivery,
                WebhookTelemetryOutcome.LeaseLost);
            return new WebhookDeliverySingleDrainResult(WebhookDeliveryDrainOutcome.AlreadyClaimed);
        }

        claim = claim with
        {
            Target = activeTarget,
            Message = activeTarget.WebhookMessage,
            LeaseExpiresAtUtc = leaseExpiresAtUtc
        };
        var governanceResolver = executionScope.ServiceProvider.GetRequiredService<IWebhookDeliveryGovernanceResolver>();
        var deliveryPolicy = await governanceResolver.ResolveAsync(claim.Target.TenantId, cancellationToken);
        var payloadBytes = claim.Message.GetPayloadBytes();
        if (payloadBytes is null)
        {
            return await AbandonTargetAsync(
                claim,
                deliveryPolicy,
                PayloadUnavailableFailureCategory,
                disableEndpoint: false,
                cancellationToken);
        }

        var localOptions = webhookOptions.CurrentValue.Local;
        if (payloadBytes.Length > localOptions.MaxPayloadBytes)
        {
            return await AbandonTargetAsync(
                claim,
                deliveryPolicy,
                PayloadTooLargeFailureCategory,
                disableEndpoint: false,
                cancellationToken);
        }

        if (!Uri.TryCreate(claim.Target.DestinationUrl, UriKind.Absolute, out var endpointUri))
        {
            return await AbandonTargetAsync(
                claim,
                deliveryPolicy,
                InvalidUrlFailureCategory,
                disableEndpoint: true,
                cancellationToken);
        }

        var safetyResult = await safetyPolicy.ValidateAsync(endpointUri, cancellationToken);
        if (!safetyResult.IsAllowed)
        {
            return await AbandonTargetAsync(
                claim,
                deliveryPolicy,
                safetyResult.FailureCategory ?? InvalidUrlFailureCategory,
                disableEndpoint: true,
                cancellationToken);
        }

        var observedAt = DateTimeOffset.UtcNow;
        if (claim.Target.CredentialValidFromUtc > observedAt ||
            claim.Target.CredentialValidUntilUtc is { } validUntilUtc && validUntilUtc <= observedAt)
        {
            return await AbandonTargetAsync(
                claim,
                deliveryPolicy,
                InvalidSecretFailureCategory,
                disableEndpoint: true,
                cancellationToken);
        }

        var secret = secretResolver.Resolve(
            claim.Target.CredentialReference,
            claim.Target.CredentialVersion);
        if (secret is null)
        {
            return await AbandonTargetAsync(
                claim,
                deliveryPolicy,
                MissingSecretFailureCategory,
                disableEndpoint: true,
                cancellationToken);
        }

        var startedAt = DateTimeOffset.UtcNow;
        var startedTimestamp = Stopwatch.GetTimestamp();
        try
        {
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timeoutSeconds = Math.Min(claim.Target.TimeoutSeconds, deliveryPolicy.EndpointTimeoutSeconds);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            using var request = BuildRequest(endpointUri, claim.Message.Id, payloadBytes, secret);
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                requestTimeout.Token);

            await DrainResponseBodyAsync(
                response.Content,
                localOptions.MaxResponsePreviewBytes,
                requestTimeout.Token);

            var completedAt = DateTimeOffset.UtcNow;
            var durationMs = GetDurationMilliseconds(startedTimestamp);
            if (response.IsSuccessStatusCode)
            {
                return await SettleTargetSucceededAsync(
                    claim,
                    startedAt,
                    completedAt,
                    (int)response.StatusCode,
                    durationMs,
                    cancellationToken);
            }

            var failureCategory = IsRedirect(response.StatusCode)
                ? RedirectResponseFailureCategory
                : HttpNonSuccessFailureCategory;
            return await SettleTargetFailureAsync(
                claim,
                deliveryPolicy,
                startedAt,
                completedAt,
                failureCategory,
                (int)response.StatusCode,
                durationMs,
                retryable: true,
                disableEndpointOnAbandon: true,
                cancellationToken,
                GetRetryAfter(response.Headers.RetryAfter, completedAt.UtcDateTime));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return await SettleTargetFailureAsync(
                claim,
                deliveryPolicy,
                startedAt,
                DateTimeOffset.UtcNow,
                TimeoutFailureCategory,
                null,
                GetDurationMilliseconds(startedTimestamp),
                retryable: true,
                disableEndpointOnAbandon: true,
                cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            return await SettleTargetFailureAsync(
                claim,
                deliveryPolicy,
                startedAt,
                DateTimeOffset.UtcNow,
                NetworkFailureCategory,
                null,
                GetDurationMilliseconds(startedTimestamp),
                retryable: true,
                disableEndpointOnAbandon: true,
                cancellationToken);
        }
        catch (FormatException)
        {
            return await SettleTargetFailureAsync(
                claim,
                deliveryPolicy,
                startedAt,
                DateTimeOffset.UtcNow,
                InvalidSecretFailureCategory,
                null,
                GetDurationMilliseconds(startedTimestamp),
                retryable: false,
                disableEndpointOnAbandon: true,
                cancellationToken);
        }
    }

    private async Task<WebhookDeliverySingleDrainResult> SettleTargetSucceededAsync(
        WebhookLocalTargetClaim claim,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        int httpStatusCode,
        int durationMs,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var targetRepository = scope.ServiceProvider.GetRequiredService<IWebhookLocalTargetRepository>();
        var attemptRepository = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryAttemptRepository>();
        var endpointRepository = scope.ServiceProvider.GetRequiredService<IWebhookEndpointRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var evidenceId = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var target = await targetRepository.GetActiveClaimAsync(
                claim.Target.TenantId,
                claim.Target.Id,
                claim.LeaseToken,
                claim.DeliveryFence,
                completedAt,
                token);
            if (target is null)
            {
                return (Guid?)null;
            }

            target.MarkSucceeded(claim.LeaseToken, claim.DeliveryFence, completedAt);
            var evidence = CreateAttemptEvidence(
                claim,
                WebhookDeliveryAttemptOutcome.Succeeded,
                startedAt,
                completedAt,
                httpStatusCode,
                durationMs,
                null,
                null);
            await attemptRepository.CreateAsync(evidence, token);
            await endpointRepository.MarkSuccessAsync(
                target.TenantId,
                target.WebhookEndpointId,
                completedAt.UtcDateTime,
                token);
            return evidence.Id;
        }, cancellationToken);

        if (evidenceId is null)
        {
            metrics.RecordWebhookProcessingOutcome(
                WebhookTelemetryProvider.Local,
                WebhookTelemetryOperation.Delivery,
                WebhookTelemetryOutcome.LeaseLost);
            return new WebhookDeliverySingleDrainResult(WebhookDeliveryDrainOutcome.AlreadyClaimed);
        }

        metrics.RecordWebhookDeliveryAttempt(claim.Message.EventType, "succeeded");
        metrics.RecordWebhookDeliverySuccess(claim.Message.EventType);
        metrics.RecordWebhookProcessingOutcome(
            WebhookTelemetryProvider.Local,
            WebhookTelemetryOperation.Delivery,
            WebhookTelemetryOutcome.Succeeded);
        return new WebhookDeliverySingleDrainResult(WebhookDeliveryDrainOutcome.Succeeded, evidenceId);
    }

    private async Task<WebhookDeliverySingleDrainResult> SettleTargetFailureAsync(
        WebhookLocalTargetClaim claim,
        WebhookDeliveryGovernancePolicy deliveryPolicy,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string failureCategory,
        int? httpStatusCode,
        int durationMs,
        bool retryable,
        bool disableEndpointOnAbandon,
        CancellationToken cancellationToken,
        TimeSpan? retryAfter = null)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var targetRepository = scope.ServiceProvider.GetRequiredService<IWebhookLocalTargetRepository>();
        var attemptRepository = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryAttemptRepository>();
        var endpointRepository = scope.ServiceProvider.GetRequiredService<IWebhookEndpointRepository>();
        var auditWriter = scope.ServiceProvider.GetRequiredService<IWebhookAuditEventWriter>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var currentAttemptNumber = checked((int)claim.DeliveryFence);
        var nextAttemptNumber = checked(currentAttemptNumber + 1);
        var maxAttempts = Math.Min(
            Math.Min(claim.Target.MaxAttempts, deliveryPolicy.MaxAttempts),
            retryScheduler.MaxScheduledAttempts);
        var retryPermitted = retryable && retryScheduler.CanScheduleAttempt(nextAttemptNumber, maxAttempts);
        var nextRetryAt = retryPermitted
            ? retryScheduler.GetScheduledAtUtc(nextAttemptNumber, completedAt.UtcDateTime, retryAfter)
            : (DateTime?)null;
        var autoPauseThreshold = disableEndpointOnAbandon && !retryable
            ? 1
            : deliveryPolicy.AutoPauseThreshold;

        var mutation = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var target = await targetRepository.GetActiveClaimAsync(
                claim.Target.TenantId,
                claim.Target.Id,
                claim.LeaseToken,
                claim.DeliveryFence,
                completedAt,
                token);
            if (target is null)
            {
                return (
                    EvidenceId: (Guid?)null,
                    RetryScheduled: false,
                    DeadLettered: false,
                    AutoPauseTransitioned: false);
            }

            var failureState = await endpointRepository.RecordFailureAsync(
                target.TenantId,
                target.WebhookEndpointId,
                target.Id,
                claim.LeaseToken,
                claim.DeliveryFence,
                completedAt.UtcDateTime,
                failureCategory,
                autoPauseThreshold,
                token);
            if (retryPermitted && nextRetryAt is { } scheduledAt)
            {
                target.ScheduleRetry(
                    claim.LeaseToken,
                    claim.DeliveryFence,
                    new DateTimeOffset(DateTime.SpecifyKind(scheduledAt, DateTimeKind.Utc)),
                    completedAt);
            }
            else if (retryable)
            {
                target.DeadLetter(claim.LeaseToken, claim.DeliveryFence, completedAt);
            }
            else
            {
                target.Abandon(claim.LeaseToken, claim.DeliveryFence, completedAt);
            }

            var evidence = CreateAttemptEvidence(
                claim,
                retryable ? WebhookDeliveryAttemptOutcome.Failed : WebhookDeliveryAttemptOutcome.Abandoned,
                startedAt,
                completedAt,
                httpStatusCode,
                durationMs,
                failureCategory,
                nextRetryAt);
            await attemptRepository.CreateAsync(evidence, token);

            if (failureState.TransitionedToAutoPaused)
            {
                await auditWriter.AppendAsync(
                    new WebhookAuditWriteRequest(
                        target.TenantId,
                        WebhookAuditAction.EndpointAutoPaused,
                        WebhookAuditTargetKind.Endpoint,
                        target.WebhookEndpointId,
                        "automatic_circuit_opened",
                        WebhookAuditOutcome.Succeeded,
                        SafeBeforeJson: JsonSerializer.Serialize(new
                        {
                            status = WebhookEndpointStatus.Active.ToString(),
                            consecutiveFailureCount = Math.Max(0, failureState.ConsecutiveFailureCount - 1)
                        }),
                        SafeAfterJson: JsonSerializer.Serialize(new
                        {
                            status = WebhookEndpointStatus.AutoPaused.ToString(),
                            failureState.ConsecutiveFailureCount,
                            failureCategory,
                            autoPauseThreshold
                        }),
                        ConfigurationVersion: deliveryPolicy.ResolutionVersion,
                        PrincipalKind: WebhookAuditPrincipalKind.System,
                        PrincipalReference: "system:webhook-delivery-worker"),
                    token);
            }

            return (
                EvidenceId: (Guid?)evidence.Id,
                RetryScheduled: retryPermitted,
                DeadLettered: retryable && !retryPermitted,
                AutoPauseTransitioned: failureState.TransitionedToAutoPaused);
        }, cancellationToken);

        if (mutation.EvidenceId is null)
        {
            metrics.RecordWebhookProcessingOutcome(
                WebhookTelemetryProvider.Local,
                WebhookTelemetryOperation.Delivery,
                WebhookTelemetryOutcome.LeaseLost);
            return new WebhookDeliverySingleDrainResult(WebhookDeliveryDrainOutcome.AlreadyClaimed);
        }

        if (mutation.AutoPauseTransitioned)
        {
            metrics.RecordWebhookEndpointDisabled(failureCategory);
            metrics.RecordWebhookEndpointAutoPause(WebhookTelemetryProvider.Local);
        }

        var outcome = mutation.RetryScheduled ? "retry_scheduled" : "abandoned";
        metrics.RecordWebhookDeliveryAttempt(claim.Message.EventType, outcome, failureCategory);
        metrics.RecordWebhookDeliveryFailure(claim.Message.EventType, outcome, failureCategory);
        var telemetryOutcome = mutation.RetryScheduled
            ? WebhookTelemetryOutcome.RetryScheduled
            : mutation.DeadLettered
                ? WebhookTelemetryOutcome.DeadLettered
                : WebhookTelemetryOutcome.Abandoned;
        metrics.RecordWebhookProcessingOutcome(
            WebhookTelemetryProvider.Local,
            WebhookTelemetryOperation.Delivery,
            telemetryOutcome);
        if (mutation.RetryScheduled)
        {
            metrics.RecordWebhookRetryScheduled(
                WebhookTelemetryProvider.Local,
                WebhookTelemetryOperation.Delivery);
        }
        else if (mutation.DeadLettered)
        {
            metrics.RecordWebhookDeadLetter(
                WebhookTelemetryProvider.Local,
                WebhookTelemetryOperation.Delivery);
        }
        logger.LogWarning(
            "Webhook Local target {TargetId} attempt {AttemptNumber} failed with {FailureCategory}; Outcome={Outcome}",
            claim.Target.Id,
            currentAttemptNumber,
            failureCategory,
            outcome);
        return new WebhookDeliverySingleDrainResult(
            mutation.RetryScheduled
                ? WebhookDeliveryDrainOutcome.RetryScheduled
                : WebhookDeliveryDrainOutcome.Abandoned,
            mutation.EvidenceId);
    }

    private Task<WebhookDeliverySingleDrainResult> AbandonTargetAsync(
        WebhookLocalTargetClaim claim,
        WebhookDeliveryGovernancePolicy deliveryPolicy,
        string failureCategory,
        bool disableEndpoint,
        CancellationToken cancellationToken)
    {
        var observedAt = DateTimeOffset.UtcNow;
        return SettleTargetFailureAsync(
            claim,
            deliveryPolicy,
            observedAt,
            observedAt,
            failureCategory,
            null,
            0,
            retryable: false,
            disableEndpointOnAbandon: disableEndpoint,
            cancellationToken);
    }

    private static WebhookDeliveryAttempt CreateAttemptEvidence(
        WebhookLocalTargetClaim claim,
        WebhookDeliveryAttemptOutcome outcome,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        int? httpStatusCode,
        int durationMs,
        string? failureCategory,
        DateTime? nextRetryAt) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = claim.Target.TenantId,
            MessageId = claim.Target.WebhookMessageId,
            EndpointId = claim.Target.WebhookEndpointId,
            AttemptNumber = checked((int)claim.DeliveryFence),
            Outcome = outcome,
            ScheduledAt = claim.ClaimedAtUtc.UtcDateTime,
            SentAt = startedAt.UtcDateTime,
            CompletedAt = completedAt.UtcDateTime,
            ProcessingFence = claim.DeliveryFence,
            HttpStatusCode = httpStatusCode,
            FailureCategory = failureCategory,
            DurationMs = durationMs,
            NextRetryAt = nextRetryAt,
            CreatedAt = completedAt.UtcDateTime
        };

    private HttpRequestMessage BuildRequest(
        Uri endpointUri,
        Guid messageId,
        byte[] payloadBytes,
        WebhookSecretMaterial secret)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var signatureHeaders = signatureService.Sign(messageId.ToString("N"), timestamp, payloadBytes, secret);

        var request = new HttpRequestMessage(HttpMethod.Post, endpointUri)
        {
            Content = new ByteArrayContent(payloadBytes)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        request.Headers.TryAddWithoutValidation("svix-id", signatureHeaders.SvixId);
        request.Headers.TryAddWithoutValidation("svix-timestamp", signatureHeaders.SvixTimestamp);
        request.Headers.TryAddWithoutValidation("svix-signature", signatureHeaders.SvixSignature);
        return request;
    }

    private void RecordManualRetry(
        WebhookDeliveryAttempt attempt,
        string outcome,
        string? failureCategory = null)
    {
        metrics.RecordWebhookManualRetry(
            attempt.Message?.EventType,
            outcome,
            failureCategory);
    }

    private bool ShouldProcessLocalTargets()
    {
        var options = webhookOptions.CurrentValue;
        return !options.IsDisabled
            && (options.IsProvider(WebhookOptions.ProviderLocal)
                || options.IsProvider(WebhookOptions.ProviderComposite));
    }

    private static async Task DrainResponseBodyAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (maximumBytes <= 0 || content.Headers.ContentLength == 0)
        {
            return;
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[maximumBytes];
        var totalBytesRead = 0;
        while (totalBytesRead < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(
                buffer.AsMemory(totalBytesRead, buffer.Length - totalBytesRead),
                cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            totalBytesRead += bytesRead;
        }
    }

    private TimeSpan? GetRetryAfter(
        System.Net.Http.Headers.RetryConditionHeaderValue? header,
        DateTime responseReceivedAt)
    {
        if (header is null || webhookOptions.CurrentValue.Local.MaxRetryAfterSeconds == 0)
        {
            return null;
        }

        var delay = header.Delta
            ?? (header.Date is { } retryAt
                ? retryAt - new DateTimeOffset(DateTime.SpecifyKind(responseReceivedAt, DateTimeKind.Utc))
                : null);
        return delay is { } value && value > TimeSpan.Zero ? value : null;
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        var status = (int)statusCode;
        return status is >= 300 and <= 399;
    }

    private static int GetDurationMilliseconds(long startedTimestamp)
    {
        var elapsed = Stopwatch.GetElapsedTime(startedTimestamp);
        if (elapsed.TotalMilliseconds >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return Math.Max(0, (int)elapsed.TotalMilliseconds);
    }

}
