using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Infrastructure;
using Microsoft.Extensions.Options;

namespace Explore.API.BackgroundServices;

/// <summary>
/// Background worker that polls <see cref="OutboxMessage"/> entries and dispatches them
/// via <see cref="IOutboxMessageDispatcher"/>. Implements at-least-once delivery with
/// optimistic locking, exponential backoff, and dead-lettering.
/// </summary>
public sealed class OutboxProcessor(
    IServiceProvider serviceProvider,
    IOptions<OutboxProcessorSettings> settings,
    ILogger<OutboxProcessor> logger,
    IAgentBrowserWorkAdmission? agentWorkAdmission = null) : BackgroundService
{
    private readonly OutboxProcessorSettings _settings = settings.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            logger.LogInformation("Outbox processor is disabled");
            return;
        }

        logger.LogInformation(
            "Outbox processor starting with {Interval}s polling interval, batch size {BatchSize}",
            _settings.PollingIntervalSeconds, _settings.BatchSize);

        // Initial delay to let the application fully start
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                logger.LogError(
                    "Error in outbox processor loop. FailureCategory={FailureCategory}",
                    "batch_failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_settings.PollingIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("Outbox processor stopped");
    }

    internal async Task ProcessOutboxBatchAsync(CancellationToken stoppingToken)
    {
        using var work = agentWorkAdmission is null ? null : await agentWorkAdmission.EnterAsync(stoppingToken);
        IReadOnlyList<GenericOutboxClaim> genericClaims;
        await using (var admissionScope = serviceProvider.CreateAsyncScope())
        {
            genericClaims = await admissionScope.ServiceProvider.GetRequiredService<IGenericOutboxAdmission>()
                .ClaimDueAsync(_settings.BatchSize, DateTime.UtcNow, stoppingToken);
        }
        foreach (var claim in genericClaims)
        {
            stoppingToken.ThrowIfCancellationRequested();
            try
            {
                await ProcessGenericClaimAsync(claim, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                logger.LogWarning("Generic outbox claim unavailable. FailureCategory={FailureCategory}",
                    "generic_claim_unavailable");
            }
        }

        IReadOnlyList<NotificationOutboxClaim> notificationClaims;
        await using (var admissionScope = serviceProvider.CreateAsyncScope())
        {
            notificationClaims = await admissionScope.ServiceProvider
                .GetRequiredService<INotificationOutboxAdmission>()
                .ClaimDueAsync(_settings.BatchSize, DateTime.UtcNow, stoppingToken);
        }
        foreach (var claim in notificationClaims)
        {
            stoppingToken.ThrowIfCancellationRequested();
            try
            {
                await ProcessNotificationClaimAsync(claim, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // No settlement through unproven custody. The original lease expires for recovery.
                logger.LogWarning("Notification outbox claim unavailable. FailureCategory={FailureCategory}",
                    "notification_claim_unavailable");
            }
        }
    }

    internal async Task ProcessNotificationClaimAsync(
        NotificationOutboxClaim claim, CancellationToken stoppingToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        // Select the persisted source owner before resolving any Runtime repository or dispatcher.
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(claim.TenantId);
        var repository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var authority = scope.ServiceProvider.GetRequiredService<INotificationOutboxExecutionAuthority>();
        await authority.BindAsync(claim, stoppingToken);
        var message = await repository.GetClaimedNotificationAsync(
            claim.MessageId, claim.ProcessingLeaseExpiresAt, stoppingToken);
        if (message is null)
            return;
        await authority.RequireAsync(message, stoppingToken);
        var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxMessageDispatcher>();
        await DispatchClaimedMessageAsync(message, claim.ProcessingLeaseExpiresAt,
            repository, dispatcher, stoppingToken);
    }

    internal async Task ProcessGenericClaimAsync(GenericOutboxClaim claim, CancellationToken stoppingToken)
    {
        if (!GenericOutboxRoutes.Contains(claim.EventType))
            throw new InvalidOperationException("generic_outbox_requires_admission");
        await using var preparation = serviceProvider.CreateAsyncScope();
        await using var execution = await preparation.ServiceProvider
            .GetRequiredService<IGenericOutboxExecutionScopeFactory>().CreateAsync(claim, stoppingToken);
        var repository = execution.Services.GetRequiredService<IOutboxRepository>();
        var authority = execution.Services.GetRequiredService<IGenericOutboxExecutionAuthority>();
        var message = await repository.GetClaimedGenericAsync(
            claim.MessageId, claim.LeaseExpiresAt, claim.Status,
            execution.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime, stoppingToken);
        if (message is null)
            return;
        if (claim.Status == OutboxMessageStatus.DeadLettered)
        {
            await authority.RequireDeadLetterAsync(message, stoppingToken);
            await ReconcileDeadLetterAsync(message, claim.LeaseExpiresAt, repository,
                execution.Services.GetRequiredService<IOutboxMessageDispatcher>(), authority, stoppingToken);
            return;
        }
        await authority.RequireAsync(message, stoppingToken);
        await DispatchClaimedMessageAsync(message, claim.LeaseExpiresAt, repository,
            execution.Services.GetRequiredService<IOutboxMessageDispatcher>(), stoppingToken, authority);
    }

    private async Task DispatchClaimedMessageAsync(
        OutboxMessage message,
        DateTime processingLeaseExpiresAt,
        IOutboxRepository outboxRepository,
        IOutboxMessageDispatcher dispatcher,
        CancellationToken stoppingToken,
        IGenericOutboxExecutionAuthority? genericAuthority = null)
    {
        try
        {
            if (_settings.VerboseLogging)
            {
                logger.LogDebug("Dispatching outbox message");
            }

            // Dispatch to consumer — idempotent consumers required (at-least-once delivery)
            message.Status = OutboxMessageStatus.Processing;
            message.NextRetryAt = processingLeaseExpiresAt;
            await dispatcher.DispatchAsync(message, stoppingToken);
            if (genericAuthority is not null)
                await genericAuthority.RequireAsync(message, stoppingToken);

            var completed = await outboxRepository.MarkAsCompleted(
                message.Id,
                processingLeaseExpiresAt,
                stoppingToken);

            if (!completed)
            {
                logger.LogWarning("Outbox message was dispatched after its processing claim was replaced; completion was ignored");
                return;
            }

            logger.LogInformation("Successfully dispatched outbox message");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogError(
                "Exception dispatching outbox message. FailureCategory={FailureCategory}",
                "dispatch_failed");

            var retryDelay = _settings.CalculateRetryDelay(message.RetryCount);
            var transition = await outboxRepository.MarkAsFailed(
                message.Id,
                processingLeaseExpiresAt,
                "dispatch_failed",
                isRetryable: true,
                retryDelay,
                DateTime.UtcNow,
                stoppingToken);

            if (transition == OutboxFailureTransition.DeadLettered)
            {
                // Notification fanout has no dead-letter reconciliation route.
                // Retain its terminal row for operator investigation, never re-dispatch it.
                if (genericAuthority is not null && GenericOutboxRoutes.SupportsDeadLetter(message.EventType))
                {
                    var terminal = await outboxRepository.GetClaimedGenericAsync(
                        message.Id, processingLeaseExpiresAt, OutboxMessageStatus.DeadLettered,
                        serviceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime, stoppingToken);
                    if (terminal is not null)
                        await ReconcileDeadLetterAsync(terminal, processingLeaseExpiresAt,
                            outboxRepository, dispatcher, genericAuthority, stoppingToken);
                }
                logger.LogError(
                    "Outbox message dead-lettered after {Retries} attempts",
                    message.RetryCount + 1);
            }
            else if (transition == OutboxFailureTransition.RetryScheduled)
            {
                logger.LogWarning(
                    "Outbox message failed (retry {Retry}/{Max}). Next retry in {Delay}s",
                    message.RetryCount + 1, message.MaxRetries, retryDelay);
            }
            else if (transition == OutboxFailureTransition.NotOwned)
            {
                logger.LogWarning("Outbox message failed after its processing claim was replaced; failure was ignored");
            }
        }
    }

    private async Task ReconcileDeadLetterAsync(
        OutboxMessage message,
        DateTime processingLeaseExpiresAt,
        IOutboxRepository outboxRepository,
        IOutboxMessageDispatcher dispatcher,
        IGenericOutboxExecutionAuthority authority,
        CancellationToken stoppingToken)
    {
        try
        {
            await authority.RequireDeadLetterAsync(message, stoppingToken);
            await dispatcher.ReconcileDeadLetterAsync(message, stoppingToken);
            await authority.RequireDeadLetterAsync(message, stoppingToken);
            var reconciled = await outboxRepository.MarkDeadLetterReconciled(
                message.Id,
                processingLeaseExpiresAt,
                stoppingToken);
            if (!reconciled)
            {
                logger.LogWarning("Outbox dead-letter reconciliation completed after its claim was replaced; acknowledgement was ignored");
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogError(
                "Outbox dead-letter reconciliation failed. FailureCategory={FailureCategory}",
                "reconciliation_failed");
        }
    }
}
