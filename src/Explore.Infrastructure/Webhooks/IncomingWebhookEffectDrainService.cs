using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Services.Webhooks;
using Explore.Application.Telemetry;
using Explore.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Webhooks;

public sealed class IncomingWebhookEffectDrainService(
    IServiceScopeFactory scopeFactory,
    IIncomingWebhookEffectClaimExecutor claimExecutor,
    IOptions<IncomingWebhookProcessingSettings> settings,
    TimeProvider timeProvider,
    BusinessMetrics metrics,
    ILogger<IncomingWebhookEffectDrainService> logger) : IIncomingWebhookEffectDrainService
{
    private readonly IncomingWebhookProcessingSettings _settings = settings.Value;

    public async Task<IncomingWebhookDrainResult> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var claims = await ClaimBatchAsync(cancellationToken);
        if (claims.Count == 0)
        {
            return new IncomingWebhookDrainResult(0, 0, 0, 0, 0);
        }

        metrics.RecordWebhookProcessingOutcome(
            WebhookTelemetryProvider.Coop,
            WebhookTelemetryOperation.IncomingEffect,
            WebhookTelemetryOutcome.Claimed,
            claims.Count);

        var completed = 0;
        var leaseLost = 0;
        var authorizationDenied = 0;
        var failed = 0;

        await Parallel.ForEachAsync(
            claims,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = _settings.MaxConcurrentItems
            },
            async (claim, token) =>
            {
                try
                {
                    var result = await ExecuteWithLeaseRenewalAsync(claim, token);
                    switch (result.Outcome)
                    {
                        case IncomingWebhookClaimExecutionOutcome.Completed:
                            Interlocked.Increment(ref completed);
                            RecordCompletedOutcome(result.FailureCategory);
                            break;
                        case IncomingWebhookClaimExecutionOutcome.LeaseLost:
                            Interlocked.Increment(ref leaseLost);
                            metrics.RecordWebhookProcessingOutcome(
                                WebhookTelemetryProvider.Coop,
                                WebhookTelemetryOperation.IncomingEffect,
                                WebhookTelemetryOutcome.LeaseLost);
                            break;
                        case IncomingWebhookClaimExecutionOutcome.AuthorizationDenied:
                            Interlocked.Increment(ref authorizationDenied);
                            metrics.RecordWebhookProcessingOutcome(
                                WebhookTelemetryProvider.Coop,
                                WebhookTelemetryOperation.IncomingEffect,
                                WebhookTelemetryOutcome.Failed);
                            break;
                        default:
                            Interlocked.Increment(ref failed);
                            break;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Interlocked.Increment(ref failed);
                    logger.LogError(
                        "Incoming Coop effect processing failed. FailureType={FailureType}",
                        exception.GetType().Name);
                }
            });

        return new IncomingWebhookDrainResult(
            claims.Count,
            completed,
            leaseLost,
            authorizationDenied,
            failed);
    }

    private async Task<IReadOnlyList<IncomingWebhookEffectClaim>> ClaimBatchAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IIncomingWebhookEffectOutboxRepository>();
        var claimedAt = GetUtcNow();
        return await repository.ClaimDueAsync(
            new IncomingWebhookEffectClaimRequest(
                CreateLeaseOwner(),
                _settings.BatchSize,
                claimedAt,
                TimeSpan.FromSeconds(_settings.LeaseSeconds)),
            cancellationToken);
    }

    private async Task<IncomingWebhookClaimExecutionResult> ExecuteWithLeaseRenewalAsync(
        IncomingWebhookEffectClaim claim,
        CancellationToken cancellationToken)
    {
        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var leaseLost = 0;
        var renewalTask = RenewLeaseUntilCancelledAsync(
            claim,
            executionCancellation,
            () => Interlocked.Exchange(ref leaseLost, 1));

        try
        {
            return await claimExecutor.ExecuteAsync(claim, executionCancellation.Token);
        }
        catch (OperationCanceledException) when (
            Volatile.Read(ref leaseLost) == 1 &&
            !cancellationToken.IsCancellationRequested)
        {
            return IncomingWebhookClaimExecutionResult.LeaseLost();
        }
        finally
        {
            executionCancellation.Cancel();
            try
            {
                await renewalTask;
            }
            catch (OperationCanceledException) when (executionCancellation.IsCancellationRequested)
            {
            }
        }
    }

    private async Task RenewLeaseUntilCancelledAsync(
        IncomingWebhookEffectClaim claim,
        CancellationTokenSource executionCancellation,
        Action markLeaseLost)
    {
        var renewalInterval = TimeSpan.FromSeconds(Math.Max(1, _settings.LeaseSeconds / 3));
        using var timer = new PeriodicTimer(renewalInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(executionCancellation.Token))
        {
            var observedAt = GetUtcNow();
            var leaseExpiresAt = observedAt.AddSeconds(_settings.LeaseSeconds);
            bool renewed;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var repository = scope.ServiceProvider.GetRequiredService<IIncomingWebhookEffectOutboxRepository>();
                renewed = await repository.TryRenewClaimAsync(
                    claim,
                    observedAt,
                    leaseExpiresAt,
                    executionCancellation.Token);
            }
            catch (OperationCanceledException) when (executionCancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Incoming Coop effect lease renewal failed. FailureType={FailureType}",
                    exception.GetType().Name);
                renewed = false;
            }

            if (renewed)
            {
                continue;
            }

            markLeaseLost();
            await executionCancellation.CancelAsync();
            return;
        }
    }

    private static string CreateLeaseOwner()
    {
        var owner = $"incoming-coop-effect:{Environment.MachineName}:{Environment.ProcessId}";
        return owner.Length <= IncomingWebhookEffectOutbox.MaxLeaseOwnerLength
            ? owner
            : owner[..IncomingWebhookEffectOutbox.MaxLeaseOwnerLength];
    }

    private void RecordCompletedOutcome(string? outcomeCategory)
    {
        var outcome = outcomeCategory switch
        {
            "retry_scheduled" => WebhookTelemetryOutcome.RetryScheduled,
            "dead_lettered" => WebhookTelemetryOutcome.DeadLettered,
            "recovered" => WebhookTelemetryOutcome.Recovered,
            _ => WebhookTelemetryOutcome.Succeeded
        };
        metrics.RecordWebhookProcessingOutcome(
            WebhookTelemetryProvider.Coop,
            WebhookTelemetryOperation.IncomingEffect,
            outcome);
        if (outcome == WebhookTelemetryOutcome.RetryScheduled)
        {
            metrics.RecordWebhookRetryScheduled(
                WebhookTelemetryProvider.Coop,
                WebhookTelemetryOperation.IncomingEffect);
        }
        else if (outcome == WebhookTelemetryOutcome.DeadLettered)
        {
            metrics.RecordWebhookDeadLetter(
                WebhookTelemetryProvider.Coop,
                WebhookTelemetryOperation.IncomingEffect);
        }
    }

    private DateTime GetUtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
