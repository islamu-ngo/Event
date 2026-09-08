using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
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
    ILogger<OutboxProcessor> logger) : BackgroundService
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
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in outbox processor loop");
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

    private async Task ProcessOutboxBatchAsync(CancellationToken stoppingToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();

        var outboxRepository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxMessageDispatcher>();

        var pendingMessages = await outboxRepository.GetPendingBatch(_settings.BatchSize, stoppingToken);

        if (pendingMessages.Count == 0)
        {
            if (_settings.VerboseLogging)
            {
                logger.LogDebug("No pending outbox messages");
            }
            return;
        }

        logger.LogInformation("Processing {Count} outbox messages", pendingMessages.Count);

        foreach (var message in pendingMessages)
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            await ProcessSingleMessageAsync(message, outboxRepository, dispatcher, stoppingToken);
        }
    }

    internal async Task ProcessSingleMessageAsync(
        OutboxMessage message,
        IOutboxRepository outboxRepository,
        IOutboxMessageDispatcher dispatcher,
        CancellationToken stoppingToken)
    {
        if (message.Status == OutboxMessageStatus.DeadLettered)
        {
            var reconciliationLeaseExpiresAt = await outboxRepository.TryClaimDeadLetterReconciliation(
                message.Id,
                DateTime.UtcNow,
                stoppingToken);
            if (reconciliationLeaseExpiresAt is not null)
            {
                await ReconcileDeadLetterAsync(
                    message,
                    reconciliationLeaseExpiresAt.Value,
                    outboxRepository,
                    dispatcher,
                    stoppingToken);
            }
            return;
        }

        var processingLeaseExpiresAt = await outboxRepository.TryClaimForProcessing(
            message.Id,
            DateTime.UtcNow,
            stoppingToken);
        if (processingLeaseExpiresAt is null)
        {
            if (_settings.VerboseLogging)
            {
                logger.LogDebug("Message {Id} already being processed by another worker", message.Id);
            }
            return;
        }

        try
        {
            if (_settings.VerboseLogging)
            {
                logger.LogDebug(
                    "Dispatching message {Id}: {EventType} for {AggregateType}/{AggregateId}",
                    message.Id, message.EventType, message.AggregateType, message.AggregateId);
            }

            // Dispatch to consumer — idempotent consumers required (at-least-once delivery)
            await dispatcher.DispatchAsync(message, stoppingToken);

            var completed = await outboxRepository.MarkAsCompleted(
                message.Id,
                processingLeaseExpiresAt.Value,
                stoppingToken);

            if (!completed)
            {
                logger.LogWarning(
                    "Message {Id} was dispatched after its processing claim was replaced; completion was ignored",
                    message.Id);
                return;
            }

            logger.LogInformation(
                "Successfully dispatched message {Id}: {EventType} for {AggregateType}/{AggregateId}",
                message.Id, message.EventType, message.AggregateType, message.AggregateId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Exception dispatching message {Id}: {ExceptionType}",
                message.Id,
                ex.GetType().Name);

            var retryDelay = _settings.CalculateRetryDelay(message.RetryCount);
            var transition = await outboxRepository.MarkAsFailed(
                message.Id,
                processingLeaseExpiresAt.Value,
                "dispatch_failed",
                isRetryable: true,
                retryDelay,
                DateTime.UtcNow,
                stoppingToken);

            if (transition == OutboxFailureTransition.DeadLettered)
            {
                await ReconcileDeadLetterAsync(
                    message,
                    processingLeaseExpiresAt.Value,
                    outboxRepository,
                    dispatcher,
                    stoppingToken);
                logger.LogError(
                    "Message {Id} dead-lettered after {Retries} attempts",
                    message.Id, message.RetryCount + 1);
            }
            else if (transition == OutboxFailureTransition.RetryScheduled)
            {
                logger.LogWarning(
                    "Message {Id} failed (retry {Retry}/{Max}). Next retry in {Delay}s",
                    message.Id, message.RetryCount + 1, message.MaxRetries, retryDelay);
            }
            else if (transition == OutboxFailureTransition.NotOwned)
            {
                logger.LogWarning(
                    "Message {Id} failed after its processing claim was replaced; failure was ignored",
                    message.Id);
            }
        }
    }

    private async Task ReconcileDeadLetterAsync(
        OutboxMessage message,
        DateTime processingLeaseExpiresAt,
        IOutboxRepository outboxRepository,
        IOutboxMessageDispatcher dispatcher,
        CancellationToken stoppingToken)
    {
        try
        {
            await dispatcher.ReconcileDeadLetterAsync(message, stoppingToken);
            var reconciled = await outboxRepository.MarkDeadLetterReconciled(
                message.Id,
                processingLeaseExpiresAt,
                stoppingToken);
            if (!reconciled)
            {
                logger.LogWarning(
                    "Dead-letter reconciliation for message {Id} completed after its claim was replaced; acknowledgement was ignored",
                    message.Id);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Dead-letter reconciliation failed for message {Id}: {ExceptionType}",
                message.Id,
                ex.GetType().Name);
        }
    }
}
