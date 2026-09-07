using System.Collections.Concurrent;
using System.Net;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure;

public sealed class EmailDispatchDrainService(
    IServiceScopeFactory scopeFactory,
    IOptions<EmailDispatchProcessorSettings> settings,
    BusinessMetrics metrics,
    ILogger<EmailDispatchDrainService> logger) : IEmailDispatchDrainService, IDisposable
{
    private const string ProcessingLeaseExpiredFailureCategory = "processing_lease_expired";
    private const string ProcessingLeaseExpiredMessage = "Email dispatch processing lease expired before a durable outcome was recorded. Outcome is unknown and requires operator review or replay.";
    private const string ProcessingLeaseReleasedFailureCategory = "processing_lease_released";
    private const string ProcessingLeaseReleasedMessage = "Email dispatch processing lease expired before provider handoff and was released for retry.";
    private const string ProcessingCancelledFailureCategory = "processing_cancelled_before_handoff";
    private const string ProcessingCancelledMessage = "Email dispatch processing was cancelled before provider handoff and was released for retry.";
    private const string AcceptedSettlementUnknownFailureCategory = "accepted_settlement_unknown";
    private const string SmtpOutcomeUnknownMessage = "SMTP provider acceptance is uncertain. Automatic resend is disabled pending reconciliation.";
    private const string SmtpSendFailedMessage = "SMTP send failed before provider acceptance was confirmed.";
    private const string SmtpRateDeferredFailureCategory = "smtp_rate_deferred";

    private readonly EmailDispatchProcessorSettings _settings = settings.Value;
    private readonly SemaphoreSlim _batchGate = new(1, 1);

    public async Task<EmailDispatchDrainResult> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        if (!await _batchGate.WaitAsync(0, cancellationToken))
        {
            logger.LogDebug("Email dispatch batch skipped because another batch is active");
            return new EmailDispatchDrainResult(0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        try
        {
            return await ProcessBatchCoreAsync(cancellationToken);
        }
        finally
        {
            _batchGate.Release();
        }
    }

    private async Task<EmailDispatchDrainResult> ProcessBatchCoreAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IEmailDispatchOutboxRepository>();
        var now = DateTime.UtcNow;
        var pending = await repository.ClaimPendingBatchAsync(
            new EmailDispatchBatchClaimRequest(
                Guid.CreateVersion7(),
                _settings.BatchSize,
                _settings.MaxRowsPerTenantPerBatch,
                _settings.MaxConcurrentDispatches,
                _settings.MaxConcurrentDispatchesPerTenant,
                _settings.OptionalBacklogHighWatermark,
                _settings.OptionalBacklogLowWatermark,
                now),
            cancellationToken);

        if (pending.Count == 0)
        {
            if (_settings.VerboseLogging)
            {
                logger.LogDebug("No pending email dispatch rows");
            }

            return new EmailDispatchDrainResult(0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        logger.LogInformation("Processing {Count} email dispatch rows", pending.Count);

        var sent = 0;
        var retryScheduled = 0;
        var deadLettered = 0;
        var unknown = 0;
        var skipped = 0;
        var tenantPaused = 0;
        var alreadyClaimed = 0;
        var processed = 0;

        var tenantGates = pending
            .Select(dispatch => dispatch.TenantId)
            .Distinct()
            .ToDictionary(
                tenantId => tenantId,
                _ => new SemaphoreSlim(_settings.MaxConcurrentDispatchesPerTenant));

        await Parallel.ForEachAsync(
            pending,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = _settings.MaxConcurrentDispatches
            },
            async (dispatch, itemCancellationToken) =>
            {
                var tenantGate = tenantGates[dispatch.TenantId];
                await tenantGate.WaitAsync(itemCancellationToken);
                try
                {
                    var result = await ProcessClaimedDispatchAsync(dispatch, _settings.ConsumerId, itemCancellationToken);
                    switch (result.Outcome)
                    {
                        case EmailDispatchDrainOutcome.Sent:
                            Interlocked.Increment(ref sent);
                            Interlocked.Increment(ref processed);
                            break;
                        case EmailDispatchDrainOutcome.RetryScheduled:
                            Interlocked.Increment(ref retryScheduled);
                            Interlocked.Increment(ref processed);
                            break;
                        case EmailDispatchDrainOutcome.DeadLettered:
                            Interlocked.Increment(ref deadLettered);
                            Interlocked.Increment(ref processed);
                            break;
                        case EmailDispatchDrainOutcome.Unknown:
                            Interlocked.Increment(ref unknown);
                            Interlocked.Increment(ref processed);
                            break;
                        case EmailDispatchDrainOutcome.Skipped:
                            Interlocked.Increment(ref skipped);
                            Interlocked.Increment(ref processed);
                            break;
                        case EmailDispatchDrainOutcome.TenantPaused:
                            Interlocked.Increment(ref tenantPaused);
                            break;
                        case EmailDispatchDrainOutcome.AlreadyClaimed:
                            Interlocked.Increment(ref alreadyClaimed);
                            break;
                        case EmailDispatchDrainOutcome.Deferred:
                            Interlocked.Increment(ref retryScheduled);
                            Interlocked.Increment(ref processed);
                            break;
                    }
                }
                finally
                {
                    tenantGate.Release();
                }
            });

        foreach (var tenantGate in tenantGates.Values)
        {
            tenantGate.Dispose();
        }

        return new EmailDispatchDrainResult(
            pending.Count,
            processed,
            sent,
            retryScheduled,
            deadLettered,
            unknown,
            skipped,
            tenantPaused,
            alreadyClaimed);
    }

    public async Task<EmailDispatchRecoveryResult> RecoverStaleProcessingAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IEmailDispatchOutboxRepository>();
        var recoveredAt = DateTime.UtcNow;
        var processingStartedBefore = recoveredAt.AddSeconds(-_settings.ProcessingLeaseTimeoutSeconds);
        var recovered = await repository.RecoverStaleProcessing(
            new EmailDispatchStaleRecoveryRequest(
                processingStartedBefore,
                recoveredAt,
                ProcessingLeaseReleasedFailureCategory,
                ProcessingLeaseReleasedMessage,
                ProcessingLeaseExpiredFailureCategory,
                ProcessingLeaseExpiredMessage,
                _settings.BatchSize),
            cancellationToken);

        if (recovered.RecoveredCount > 0)
        {
            logger.LogWarning(
                "Recovered {RecoveredCount} stale email dispatch rows. RetryScheduled={RetryScheduledCount}, Unknown={UnknownCount}, ProcessingStartedBefore={ProcessingStartedBefore:o}",
                recovered.RecoveredCount,
                recovered.RetryScheduledCount,
                recovered.UnknownCount,
                processingStartedBefore);
        }
        else if (_settings.VerboseLogging)
        {
            logger.LogDebug(
                "No stale email dispatch processing rows found before {ProcessingStartedBefore:o}",
                processingStartedBefore);
        }

        return new EmailDispatchRecoveryResult(recovered.RecoveredCount, processingStartedBefore);
    }

    public async Task<EmailDispatchSingleDrainResult> ProcessSingleAsync(
        Guid tenantId,
        Guid publishEventId,
        string consumerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(consumerId))
        {
            throw new ArgumentException("Consumer id is required for email dispatch drainage.", nameof(consumerId));
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IEmailDispatchOutboxRepository>();
        var now = DateTime.UtcNow;
        var dispatch = await repository.TryClaimSpecificAsync(
            new EmailDispatchSpecificClaimRequest(
                tenantId,
                publishEventId,
                Guid.CreateVersion7(),
                _settings.MaxConcurrentDispatches,
                _settings.MaxConcurrentDispatchesPerTenant,
                _settings.OptionalBacklogHighWatermark,
                _settings.OptionalBacklogLowWatermark,
                now),
            cancellationToken);
        if (dispatch is not null)
        {
            return await ProcessClaimedDispatchAsync(dispatch, consumerId, cancellationToken);
        }

        dispatch = await repository.GetByTenantAndPublishEventId(tenantId, publishEventId, cancellationToken);
        if (dispatch is null)
        {
            metrics.RecordEmailDispatchRabbitMqConsume("rejected", "missing_outbox");
            logger.LogWarning(
                "Email dispatch pointer for tenant {TenantId} and publish event {PublishEventId} has no durable outbox row",
                tenantId,
                publishEventId);
            return new EmailDispatchSingleDrainResult(EmailDispatchDrainOutcome.Missing);
        }

        if (IsSettled(dispatch.Status))
        {
            metrics.RecordEmailDispatchRabbitMqConsume("acked", "already_settled");
            logger.LogInformation(
                "Email dispatch pointer {PublishEventId} for outbox row {OutboxId} is already settled with status {Status}",
                publishEventId,
                dispatch.Id,
                dispatch.Status);
            return new EmailDispatchSingleDrainResult(EmailDispatchDrainOutcome.AlreadySettled, dispatch.Id);
        }

        if (dispatch.Status == EmailDispatchStatus.Processing)
        {
            metrics.RecordEmailDispatchRabbitMqConsume("acked", "already_processing");
            return new EmailDispatchSingleDrainResult(EmailDispatchDrainOutcome.AlreadyClaimed, dispatch.Id);
        }

        if (dispatch.Status is EmailDispatchStatus.Pending or EmailDispatchStatus.RetryScheduled
            && dispatch.NextAttemptAt is { } nextAttemptAt
            && nextAttemptAt > now)
        {
            metrics.RecordEmailDispatchRabbitMqConsume("acked", "not_due");
            return new EmailDispatchSingleDrainResult(EmailDispatchDrainOutcome.Deferred, dispatch.Id);
        }

        if (await repository.IsTenantPaused(tenantId, cancellationToken))
        {
            return new EmailDispatchSingleDrainResult(EmailDispatchDrainOutcome.TenantPaused, dispatch.Id);
        }

        return new EmailDispatchSingleDrainResult(EmailDispatchDrainOutcome.Deferred, dispatch.Id);
    }

    private async Task<EmailDispatchSingleDrainResult> ProcessClaimedDispatchAsync(
        EmailDispatchOutbox dispatch,
        string consumerId,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IEmailDispatchOutboxRepository>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var eligibilityEvaluator = scope.ServiceProvider.GetRequiredService<IEmailDispatchEligibilityEvaluator>();
        var unsubscribeTokenService = scope.ServiceProvider.GetRequiredService<IEmailUnsubscribeTokenService>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var tenantAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();

        if (dispatch.ProcessingStartedAt is null)
        {
            throw new InvalidOperationException("A claimed email dispatch requires a processing start time.");
        }

        var now = DateTime.UtcNow;
        var leaseToken = dispatch.ProcessingLeaseToken
            ?? throw new InvalidOperationException("A claimed email dispatch requires a processing lease token.");

        var providerHandoffStarted = false;
        var attemptNumber = dispatch.AttemptCount;
        tenantAccessor.SetTenant(dispatch.TenantId);

        try
        {
            var eligibility = await eligibilityEvaluator.EvaluateAndBeginProviderHandoffAsync(
                new EmailDispatchEligibilityRequest(
                    dispatch.TenantId,
                    dispatch.Id,
                    leaseToken,
                    attemptNumber,
                    _settings.GlobalSmtpRateLimitPerMinute,
                    _settings.TenantSmtpRateLimitPerMinute,
                    consumerId,
                    now),
                cancellationToken);
            switch (eligibility.Outcome)
            {
                case EmailDispatchEligibilityOutcome.Skipped:
                    metrics.RecordEmailDispatchOperationalOutcome("skipped", eligibility.SkipReason);
                    logger.LogInformation(
                        "Email dispatch {Id} skipped before provider handoff with reason {SkipReason}",
                        dispatch.Id,
                        eligibility.SkipReason);
                    return new EmailDispatchSingleDrainResult(EmailDispatchDrainOutcome.Skipped, dispatch.Id);
                case EmailDispatchEligibilityOutcome.TenantPaused:
                    return new EmailDispatchSingleDrainResult(EmailDispatchDrainOutcome.TenantPaused, dispatch.Id);
                case EmailDispatchEligibilityOutcome.ProcessorPaused:
                    return new EmailDispatchSingleDrainResult(EmailDispatchDrainOutcome.Deferred, dispatch.Id);
                case EmailDispatchEligibilityOutcome.LostClaim:
                    return new EmailDispatchSingleDrainResult(EmailDispatchDrainOutcome.AlreadyClaimed, dispatch.Id);
                case EmailDispatchEligibilityOutcome.RateDeferred:
                    metrics.RecordEmailDispatchOperationalOutcome("rate_deferred", SmtpRateDeferredFailureCategory);
                    return new EmailDispatchSingleDrainResult(EmailDispatchDrainOutcome.Deferred, dispatch.Id);
            }

            attemptNumber = eligibility.AttemptNumber
                ?? throw new InvalidOperationException("Eligible email dispatch is missing its fenced attempt number.");
            dispatch.RecipientEmail = eligibility.RecipientEmail
                ?? throw new InvalidOperationException("Eligible email dispatch is missing its current authorized destination.");
            providerHandoffStarted = true;
            var preferenceCategory = ResolvePreferenceCategory(dispatch.Kind);
            var message = BuildEmailMessage(dispatch, unsubscribeTokenService, configuration, preferenceCategory, now);

            var result = await emailService.SendAsync(message, cancellationToken);
            var completedAt = DateTime.UtcNow;

            if (result.Success)
            {
                var settlement = new EmailDispatchAcceptedSettlement(
                    dispatch.TenantId,
                    dispatch.Id,
                    leaseToken,
                    attemptNumber,
                    completedAt,
                    ProviderMessageId: null);

                try
                {
                    await repository.SettleProviderAccepted(settlement, cancellationToken);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    return await ReconcileProviderAcceptedAsync(settlement, dispatch);
                }
                catch (OperationCanceledException)
                {
                    return await ReconcileProviderAcceptedAsync(settlement, dispatch);
                }

                metrics.RecordEmailDispatchAttempt("sent");
                logger.LogInformation(
                    "Email dispatch {Id} sent for tenant {TenantId} and source {SourceType}/{SourceId}",
                    dispatch.Id,
                    dispatch.TenantId,
                    dispatch.SourceType,
                    dispatch.SourceId);
                return new EmailDispatchSingleDrainResult(EmailDispatchDrainOutcome.Sent, dispatch.Id);
            }

            var providerError = result.ErrorMessage;
            var outcomeUnknown = IsUnknownOutcome(providerError);
            var failureCategory = outcomeUnknown ? "smtp_outcome_unknown" : "smtp_send_failed";
            var safeFailureMessage = outcomeUnknown ? SmtpOutcomeUnknownMessage : SmtpSendFailedMessage;
            if (outcomeUnknown)
            {
                return await ReconcileProviderAcceptedAsync(
                    new EmailDispatchAcceptedSettlement(
                        dispatch.TenantId,
                        dispatch.Id,
                        leaseToken,
                        attemptNumber,
                        completedAt,
                        null),
                    dispatch);
            }

            var delay = TimeSpan.FromSeconds(_settings.CalculateRetryDelay(attemptNumber));
            var settlementOutcome = await repository.SettleProviderFailure(
                new EmailDispatchFailureSettlement(
                    dispatch.TenantId,
                    dispatch.Id,
                    leaseToken,
                    attemptNumber,
                    failureCategory,
                    safeFailureMessage,
                    delay,
                    Math.Min(dispatch.MaxAttempts, _settings.MaxAttemptCount),
                    completedAt),
                cancellationToken);
            if (settlementOutcome == EmailDispatchFailureSettlementOutcome.StaleClaim)
            {
                return new EmailDispatchSingleDrainResult(EmailDispatchDrainOutcome.AlreadyClaimed, dispatch.Id);
            }

            var isRetryExhausted = settlementOutcome == EmailDispatchFailureSettlementOutcome.DeadLettered;
            var failureOutcome = isRetryExhausted ? "dead_lettered" : "retry_scheduled";
            metrics.RecordEmailDispatchAttempt(failureOutcome, failureCategory);
            logger.LogWarning(
                "Email dispatch {Id} failed on attempt {Attempt}; outcome {Outcome}; retry delay {Delay}s; failure category {FailureCategory}",
                dispatch.Id,
                attemptNumber,
                failureOutcome,
                delay.TotalSeconds,
                failureCategory);

            return new EmailDispatchSingleDrainResult(
                isRetryExhausted ? EmailDispatchDrainOutcome.DeadLettered : EmailDispatchDrainOutcome.RetryScheduled,
                dispatch.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !providerHandoffStarted)
        {
            await ReleaseCancelledClaimAsync(dispatch, leaseToken, attemptNumber);
            throw;
        }
        catch (Exception) when (providerHandoffStarted)
        {
            return await ReconcileProviderAcceptedAsync(
                new EmailDispatchAcceptedSettlement(
                    dispatch.TenantId,
                    dispatch.Id,
                    leaseToken,
                    attemptNumber,
                    DateTime.UtcNow,
                    null),
                dispatch);
        }
        catch
        {
            throw;
        }
        finally
        {
            tenantAccessor.Clear();
        }
    }

    public void Dispose()
    {
        _batchGate.Dispose();
    }

    private async Task<EmailDispatchSingleDrainResult> ReconcileProviderAcceptedAsync(
        EmailDispatchAcceptedSettlement settlement,
        EmailDispatchOutbox dispatch)
    {
        try
        {
            await using var reconciliationScope = scopeFactory.CreateAsyncScope();
            var reconciliationRepository = reconciliationScope.ServiceProvider.GetRequiredService<IEmailDispatchOutboxRepository>();
            var reconciliation = await reconciliationRepository.ReconcileProviderAccepted(
                settlement,
                CancellationToken.None);
            if (reconciliation == EmailDispatchAcceptedReconciliationOutcome.Sent)
            {
                metrics.RecordEmailDispatchAttempt("sent");
                logger.LogInformation(
                    "Email dispatch {Id} was already durably settled after an uncertain local commit",
                    dispatch.Id);
                return new EmailDispatchSingleDrainResult(EmailDispatchDrainOutcome.Sent, dispatch.Id);
            }

            if (reconciliation == EmailDispatchAcceptedReconciliationOutcome.StaleClaim)
            {
                logger.LogWarning(
                    "Rejected stale settlement for email dispatch {Id}; a newer lease or attempt owns the row",
                    dispatch.Id);
                return new EmailDispatchSingleDrainResult(EmailDispatchDrainOutcome.AlreadyClaimed, dispatch.Id);
            }
        }
        catch (Exception)
        {
            logger.LogCritical(
                "Email dispatch {Id} accepted-settlement reconciliation failed; the durable provider-handoff fence prevents automatic resend",
                dispatch.Id);
        }

        metrics.RecordEmailDispatchAttempt(
            "unknown",
            AcceptedSettlementUnknownFailureCategory);
        logger.LogWarning(
            "Email dispatch {Id} accepted-settlement outcome is unknown; automatic resend is disabled",
            dispatch.Id);
        return new EmailDispatchSingleDrainResult(EmailDispatchDrainOutcome.Unknown, dispatch.Id);
    }

    private static string ClassifyFailureCategory(string? error)
    {
        return IsUnknownOutcome(error) ? "smtp_outcome_unknown" : "smtp_send_failed";
    }

    private static bool IsUnknownOutcome(string? error)
    {
        return error?.Contains("timeout", StringComparison.OrdinalIgnoreCase) == true
            || error?.Contains("timed out", StringComparison.OrdinalIgnoreCase) == true
            || error?.Contains("operation canceled", StringComparison.OrdinalIgnoreCase) == true;
    }

    private async Task ReleaseCancelledClaimAsync(
        EmailDispatchOutbox dispatch,
        Guid leaseToken,
        int attemptNumber)
    {
        await using var releaseScope = scopeFactory.CreateAsyncScope();
        var releaseRepository = releaseScope.ServiceProvider.GetRequiredService<IEmailDispatchOutboxRepository>();
        var release = await releaseRepository.ReleaseClaimBeforeProviderHandoff(
            new EmailDispatchPreHandoffRelease(
                dispatch.TenantId,
                dispatch.Id,
                leaseToken,
                attemptNumber,
                DateTime.UtcNow,
                ProcessingCancelledFailureCategory,
                ProcessingCancelledMessage),
            CancellationToken.None);
        if (release == EmailDispatchPreHandoffReleaseOutcome.ProviderHandoffFenced)
        {
            await ReconcileProviderAcceptedAsync(
                new EmailDispatchAcceptedSettlement(
                    dispatch.TenantId,
                    dispatch.Id,
                    leaseToken,
                    attemptNumber + 1,
                    DateTime.UtcNow,
                    null),
                dispatch);
        }
    }

    private static EmailMessage BuildEmailMessage(
        EmailDispatchOutbox dispatch,
        IEmailUnsubscribeTokenService unsubscribeTokenService,
        IConfiguration configuration,
        string? preferenceCategory,
        DateTime issuedAt)
    {
        var headers = new Dictionary<string, string>
        {
            ["X-Correlation-ID"] = dispatch.CorrelationId ?? dispatch.Id.ToString(),
            ["X-Email-Dispatch-ID"] = dispatch.Id.ToString()
        };

        var plainTextBody = dispatch.PlainTextBody;
        var htmlBody = dispatch.HtmlBody;
        var unsubscribeUrl = BuildUnsubscribeUrl(dispatch, preferenceCategory, unsubscribeTokenService, configuration, issuedAt);
        if (unsubscribeUrl is not null)
        {
            headers["List-Unsubscribe"] = $"<{unsubscribeUrl}>";
            headers["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click";
            plainTextBody = AppendPlainTextUnsubscribe(plainTextBody, unsubscribeUrl);
            htmlBody = AppendHtmlUnsubscribe(htmlBody, unsubscribeUrl);
        }

        return new EmailMessage
        {
            To = dispatch.RecipientEmail,
            Subject = dispatch.Subject,
            PlainTextBody = plainTextBody,
            HtmlBody = htmlBody,
            ReplyTo = dispatch.ReplyTo,
            CustomHeaders = headers
        };
    }

    private static string? BuildUnsubscribeUrl(
        EmailDispatchOutbox dispatch,
        string? preferenceCategory,
        IEmailUnsubscribeTokenService unsubscribeTokenService,
        IConfiguration configuration,
        DateTime issuedAt)
    {
        if (preferenceCategory is null)
        {
            return null;
        }

        var publicBaseUrl = ResolvePublicBaseUrl(configuration);
        if (publicBaseUrl is null)
        {
            return null;
        }

        var token = unsubscribeTokenService.GenerateToken(new EmailUnsubscribeTokenPayload(
            dispatch.TenantId,
            dispatch.RecipientUserId,
            preferenceCategory,
            issuedAt));
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return $"{publicBaseUrl}/api/email/unsubscribe?token={Uri.EscapeDataString(token)}";
    }

    private static string? ResolvePublicBaseUrl(IConfiguration configuration)
    {
        var configured = configuration["PublicBaseUrl"]
            ?? configuration["App:PublicBaseUrl"]
            ?? configuration["Application:PublicBaseUrl"];
        if (string.IsNullOrWhiteSpace(configured)
            || !Uri.TryCreate(configured.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        return uri.ToString().TrimEnd('/');
    }

    private static string AppendPlainTextUnsubscribe(string? body, string unsubscribeUrl)
    {
        var prefix = string.IsNullOrWhiteSpace(body) ? string.Empty : body.TrimEnd() + "\n\n";
        return $"{prefix}To stop receiving this category of email, unsubscribe: {unsubscribeUrl}";
    }

    private static string AppendHtmlUnsubscribe(string? body, string unsubscribeUrl)
    {
        var encodedUrl = WebUtility.HtmlEncode(unsubscribeUrl);
        var footer = $"<p>To stop receiving this category of email, <a href=\"{encodedUrl}\">unsubscribe</a>.</p>";
        return string.IsNullOrWhiteSpace(body) ? footer : body + footer;
    }

    private static string? ResolvePreferenceCategory(EmailDispatchKind kind) => kind switch
    {
        EmailDispatchKind.RegistrationConfirmation => NotificationPreferenceCategories.RegistrationConfirmations,
        EmailDispatchKind.EventReminder => NotificationPreferenceCategories.EventReminders,
        EmailDispatchKind.OrganizerNotification => NotificationPreferenceCategories.OrganizerAnnouncements,
        EmailDispatchKind.RegistrationApproved
            or EmailDispatchKind.RegistrationRejected
            or EmailDispatchKind.WaitlistPromoted
            or EmailDispatchKind.RegistrationCancelled
            or EmailDispatchKind.RegistrationRevoked
            or EmailDispatchKind.EventCancelled
            or EmailDispatchKind.EventUpdated => NotificationPreferenceCategories.EventUpdates,
        _ => null
    };

    private static bool IsSettled(EmailDispatchStatus status)
    {
        return status is EmailDispatchStatus.Sent
            or EmailDispatchStatus.DeadLettered
            or EmailDispatchStatus.Parked
            or EmailDispatchStatus.Unknown
            or EmailDispatchStatus.Skipped;
    }
}
