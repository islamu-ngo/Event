using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Services.Webhooks;
using Explore.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Webhooks;

public sealed class IncomingWebhookDrainService(
    IServiceScopeFactory scopeFactory,
    IIncomingWebhookClaimExecutor claimExecutor,
    IOptions<IncomingWebhookProcessingSettings> settings,
    TimeProvider timeProvider,
    ILogger<IncomingWebhookDrainService> logger) : IIncomingWebhookDrainService
{
    private readonly IncomingWebhookProcessingSettings _settings = settings.Value;

    public async Task<IncomingWebhookDrainResult> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var claims = await ClaimBatchAsync(cancellationToken);
        if (claims.Count == 0)
        {
            return new IncomingWebhookDrainResult(0, 0, 0, 0, 0);
        }

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
                            break;
                        case IncomingWebhookClaimExecutionOutcome.LeaseLost:
                            Interlocked.Increment(ref leaseLost);
                            break;
                        case IncomingWebhookClaimExecutionOutcome.AuthorizationDenied:
                            Interlocked.Increment(ref authorizationDenied);
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
                        "Incoming webhook claim processing failed. FailureType={FailureType}",
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

    private async Task<IReadOnlyList<IncomingWebhookClaim>> ClaimBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IIncomingWebhookMessageRepository>();
        var claimedAt = GetUtcNow();
        return await repository.ClaimDueAsync(
            new IncomingWebhookClaimRequest(
                CreateLeaseOwner(),
                _settings.BatchSize,
                claimedAt,
                TimeSpan.FromSeconds(_settings.LeaseSeconds)),
            cancellationToken);
    }

    private async Task<IncomingWebhookClaimExecutionResult> ExecuteWithLeaseRenewalAsync(
        IncomingWebhookClaim claim,
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
        IncomingWebhookClaim claim,
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
                var repository = scope.ServiceProvider.GetRequiredService<IIncomingWebhookMessageRepository>();
                renewed = await repository.TryRenewClaimAsync(
                    claim.TenantId,
                    claim.IncomingWebhookMessageId,
                    claim.LeaseToken,
                    claim.ProcessingFence,
                    claim.ProcessingGeneration,
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
                    "Incoming webhook lease renewal failed. FailureType={FailureType}",
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
        var owner = $"incoming-webhook:{Environment.MachineName}:{Environment.ProcessId}";
        return owner.Length <= IncomingWebhookMessage.MaxLeaseOwnerLength
            ? owner
            : owner[..IncomingWebhookMessage.MaxLeaseOwnerLength];
    }

    private DateTime GetUtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
