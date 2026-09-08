using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Exceptions;
using Explore.Domain;
using Microsoft.Extensions.Options;

namespace Explore.Application.Services.Webhooks;

public sealed class IncomingWebhookProcessingService(
    IIncomingWebhookMessageRepository messageRepository,
    IIncomingWebhookEffectReceiptRepository receiptRepository,
    IUnitOfWork unitOfWork,
    IEnumerable<IIncomingWebhookHandler> handlers,
    IOptions<IncomingWebhookProcessingSettings> settings,
    TimeProvider timeProvider) : IIncomingWebhookProcessingService
{
    private readonly IncomingWebhookProcessingSettings _settings = settings.Value;
    private readonly IIncomingWebhookHandler[] _handlers = handlers.ToArray();

    public async Task<IncomingWebhookClaimExecutionResult> ProcessAsync(
        IncomingWebhookClaim claim,
        CancellationToken cancellationToken)
    {
        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var observedAt = GetUtcNow();
                var message = await messageRepository.GetActiveClaimAsync(
                    claim.TenantId,
                    claim.IncomingWebhookMessageId,
                    claim.LeaseToken,
                    claim.ProcessingFence,
                    claim.ProcessingGeneration,
                    observedAt,
                    token);
                if (message is null)
                {
                    return IncomingWebhookClaimExecutionResult.LeaseLost();
                }

                if (message.PayloadBytes.IsEmpty)
                {
                    var transitionAt = await RefreshActiveClaimAsync(message, claim, token);
                    message.RejectPermanent(
                        claim.LeaseToken,
                        claim.ProcessingFence,
                        claim.ProcessingGeneration,
                        "incoming_webhook_payload_unavailable",
                        "The retained payload is unavailable for processing.",
                        transitionAt);
                    await SaveAggregateAsync(message, token);
                    return IncomingWebhookClaimExecutionResult.Completed();
                }

                var handler = ResolveHandler(message.Provider, message.EventType);
                if (handler is null)
                {
                    var transitionAt = await RefreshActiveClaimAsync(message, claim, token);
                    message.Ignore(
                        claim.LeaseToken,
                        claim.ProcessingFence,
                        claim.ProcessingGeneration,
                        "incoming_webhook_handler_not_registered",
                        "No local effect handler is registered for this verified callback type.",
                        transitionAt);
                    await SaveAggregateAsync(message, token);
                    return IncomingWebhookClaimExecutionResult.Completed();
                }

                var effectKind = IncomingWebhookEffectReceipt.NormalizeEffectKind(handler.EffectKind);
                var existingReceipt = await receiptRepository.GetByIdentityAsync(
                    message.TenantId,
                    message.Id,
                    effectKind,
                    token);
                if (existingReceipt is not null)
                {
                    var settledAt = await RefreshActiveClaimAsync(message, claim, token);
                    message.SettleProcessed(
                        existingReceipt,
                        effectKind,
                        IncomingWebhookSettlementSource.ExistingReceipt,
                        claim.LeaseToken,
                        claim.ProcessingFence,
                        claim.ProcessingGeneration,
                        settledAt);
                    await SaveAggregateAsync(message, token);
                    return IncomingWebhookClaimExecutionResult.Completed();
                }

                var processingContext = IncomingWebhookProcessingContext.FromClaimedMessage(
                    message,
                    claim.LeaseToken,
                    claim.ProcessingFence,
                    claim.ProcessingGeneration,
                    observedAt);
                var result = await handler.HandleAsync(processingContext, token);
                var completedAt = await RefreshActiveClaimAsync(message, claim, token);

                await ApplyResultAsync(message, claim, effectKind, result, completedAt, token);
                await SaveAggregateAsync(message, token);
                return IncomingWebhookClaimExecutionResult.Completed();
            }, cancellationToken);
        }
        catch (IncomingWebhookClaimLostException)
        {
            return IncomingWebhookClaimExecutionResult.LeaseLost();
        }
        catch (IncomingWebhookEffectReceiptConflictException)
        {
            return await RecoverConcurrentReceiptAsync(claim, cancellationToken);
        }
    }

    private async Task ApplyResultAsync(
        IncomingWebhookMessage message,
        IncomingWebhookClaim claim,
        string effectKind,
        IncomingWebhookProcessingResult result,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        switch (result.Outcome)
        {
            case IncomingWebhookProcessingOutcome.Processed:
                var receipt = IncomingWebhookEffectReceipt.Create(
                    message.TenantId,
                    message.Id,
                    effectKind,
                    message.PayloadHash,
                    message.ProcessingGeneration,
                    observedAt,
                    result.SafeResultReference);
                await receiptRepository.AddAsync(receipt, cancellationToken);
                message.SettleProcessed(
                    receipt,
                    effectKind,
                    IncomingWebhookSettlementSource.EffectCommitted,
                    claim.LeaseToken,
                    claim.ProcessingFence,
                    claim.ProcessingGeneration,
                    observedAt);
                break;

            case IncomingWebhookProcessingOutcome.Ignored:
                message.Ignore(
                    claim.LeaseToken,
                    claim.ProcessingFence,
                    claim.ProcessingGeneration,
                    result.FailureCategory ?? "incoming_webhook_ignored",
                    result.SafeDetail,
                    observedAt);
                break;

            case IncomingWebhookProcessingOutcome.RejectedPermanent:
                message.RejectPermanent(
                    claim.LeaseToken,
                    claim.ProcessingFence,
                    claim.ProcessingGeneration,
                    result.FailureCategory ?? "incoming_webhook_rejected",
                    result.SafeDetail,
                    observedAt);
                break;

            case IncomingWebhookProcessingOutcome.RetryDue:
                if (message.AttemptCount >= _settings.MaxAttempts)
                {
                    message.DeadLetter(
                        claim.LeaseToken,
                        claim.ProcessingFence,
                        claim.ProcessingGeneration,
                        result.FailureCategory ?? "incoming_webhook_attempts_exhausted",
                        result.SafeDetail,
                        observedAt);
                    break;
                }

                message.ScheduleRetry(
                    claim.LeaseToken,
                    claim.ProcessingFence,
                    claim.ProcessingGeneration,
                    result.FailureCategory ?? "incoming_webhook_retry_due",
                    result.SafeDetail,
                    observedAt.Add(ComputeRetryDelay(message.AttemptCount)),
                    observedAt);
                break;

            case IncomingWebhookProcessingOutcome.DeadLettered:
                message.DeadLetter(
                    claim.LeaseToken,
                    claim.ProcessingFence,
                    claim.ProcessingGeneration,
                    result.FailureCategory ?? "incoming_webhook_dead_lettered",
                    result.SafeDetail,
                    observedAt);
                break;

            case IncomingWebhookProcessingOutcome.PointerPersisted:
                message.SettlePointerPersisted(
                    effectKind,
                    claim.LeaseToken,
                    claim.ProcessingFence,
                    claim.ProcessingGeneration,
                    observedAt);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Unsupported incoming webhook processing outcome.");
        }
    }

    private IIncomingWebhookHandler? ResolveHandler(string provider, string? eventType)
    {
        var matches = _handlers
            .Where(handler => handler.CanHandle(provider, eventType))
            .Take(2)
            .ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException("Multiple incoming webhook handlers matched one callback identity.")
        };
    }

    private TimeSpan ComputeRetryDelay(int attemptCount)
    {
        var exponent = Math.Clamp(attemptCount - 1, 0, 30);
        var multiplier = 1L << exponent;
        var seconds = Math.Min(
            checked((long)_settings.InitialRetryDelaySeconds * multiplier),
            _settings.MaxRetryDelaySeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    private async Task SaveAggregateAsync(
        IncomingWebhookMessage message,
        CancellationToken cancellationToken)
    {
        messageRepository.TrackAppendedEvidence(message);
        await messageRepository.SaveChangesAsync(cancellationToken);
    }

    private async Task<DateTime> RefreshActiveClaimAsync(
        IncomingWebhookMessage message,
        IncomingWebhookClaim claim,
        CancellationToken cancellationToken)
    {
        var observedAt = GetUtcNow();
        if (!await messageRepository.RefreshActiveClaimAsync(message, claim, observedAt, cancellationToken))
        {
            throw new IncomingWebhookClaimLostException();
        }

        return observedAt;
    }

    private Task<IncomingWebhookClaimExecutionResult> RecoverConcurrentReceiptAsync(
        IncomingWebhookClaim claim,
        CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var message = await messageRepository.GetByTenantAndIdForUpdateAsync(
                claim.TenantId,
                claim.IncomingWebhookMessageId,
                token);
            if (message is null || message.ProcessingGeneration != claim.ProcessingGeneration)
            {
                return IncomingWebhookClaimExecutionResult.LeaseLost();
            }

            var handler = ResolveHandler(message.Provider, message.EventType);
            if (handler is null)
            {
                throw new InvalidOperationException("A committed effect receipt has no matching incoming webhook handler.");
            }

            var effectKind = IncomingWebhookEffectReceipt.NormalizeEffectKind(handler.EffectKind);
            var receipt = await receiptRepository.GetByIdentityAsync(
                claim.TenantId,
                claim.IncomingWebhookMessageId,
                effectKind,
                token);
            if (receipt is null)
            {
                throw new InvalidOperationException("The conflicting effect receipt could not be recovered.");
            }

            message.RecordConcurrentReceiptRecovery(
                receipt,
                effectKind,
                claim.ProcessingGeneration,
                GetUtcNow());
            await SaveAggregateAsync(message, token);
            return IncomingWebhookClaimExecutionResult.Completed();
        }, cancellationToken);

    private DateTime GetUtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private sealed class IncomingWebhookClaimLostException : Exception
    {
    }
}
