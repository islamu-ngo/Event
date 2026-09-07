using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Infrastructure.Integrations.Listmonk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure;

public sealed class IntegrationSyncDrainService(
    IServiceScopeFactory scopeFactory,
    IOptions<IntegrationSyncProcessorSettings> settings,
    ILogger<IntegrationSyncDrainService> logger) : IIntegrationSyncDrainService
{
    private const string PrivacyErasureFencedMessage = "Integration sync was not sent because the subscriber is subject to privacy erasure.";

    private readonly IntegrationSyncProcessorSettings _settings = settings.Value;

    public async Task<IntegrationSyncDrainResult> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IIntegrationSyncOutboxRepository>();
        DateTime now = DateTime.UtcNow;
        var pending = await repository.GetPendingBatch(
            _settings.BatchSize,
            now,
            now.AddSeconds(-_settings.ProcessingLeaseTimeoutSeconds),
            cancellationToken);

        var completed = 0;
        var retryScheduled = 0;
        var deadLettered = 0;
        var ambiguous = 0;
        var alreadyClaimed = 0;

        foreach (var outbox in pending)
        {
            var result = await ProcessOutboxAsync(outbox, cancellationToken);
            switch (result.Outcome)
            {
                case IntegrationSyncDrainOutcome.Completed:
                    completed++;
                    break;
                case IntegrationSyncDrainOutcome.RetryScheduled:
                    retryScheduled++;
                    break;
                case IntegrationSyncDrainOutcome.DeadLettered:
                    deadLettered++;
                    break;
                case IntegrationSyncDrainOutcome.Ambiguous:
                    ambiguous++;
                    break;
                case IntegrationSyncDrainOutcome.AlreadyClaimed:
                    alreadyClaimed++;
                    break;
            }
        }

        if (_settings.VerboseLogging && pending.Count > 0)
        {
            logger.LogInformation(
                "Integration sync processed {Processed}/{Pending} rows: {Completed} completed, {RetryScheduled} retry, {DeadLettered} dead-lettered, {Ambiguous} ambiguous, {AlreadyClaimed} already claimed",
                pending.Count - alreadyClaimed,
                pending.Count,
                completed,
                retryScheduled,
                deadLettered,
                ambiguous,
                alreadyClaimed);
        }

        return new IntegrationSyncDrainResult(
            pending.Count,
            pending.Count - alreadyClaimed,
            completed,
            retryScheduled,
            deadLettered,
            ambiguous,
            alreadyClaimed);
    }

    private async Task<IntegrationSyncSingleDrainResult> ProcessOutboxAsync(
        IntegrationSyncOutbox outbox,
        CancellationToken cancellationToken)
    {
        if (outbox.Status == IntegrationSyncStatus.Processing &&
            (outbox.ProcessingLeaseToken is null || outbox.ProcessingStartedAt is null))
        {
            await using var malformedScope = scopeFactory.CreateAsyncScope();
            var malformedRepository = malformedScope.ServiceProvider.GetRequiredService<IIntegrationSyncOutboxRepository>();
            bool parked = await malformedRepository.ParkMalformedProcessingAsync(
                outbox.TenantId,
                outbox.Id,
                DateTime.UtcNow,
                cancellationToken);
            return new IntegrationSyncSingleDrainResult(
                parked ? IntegrationSyncDrainOutcome.Ambiguous : IntegrationSyncDrainOutcome.AlreadyClaimed,
                outbox.Id);
        }

        if (outbox.Status == IntegrationSyncStatus.Processing &&
            outbox.LastError == IntegrationSyncFailureCodes.ProviderHandoffInDoubt &&
            outbox.ProcessingLeaseToken is Guid inDoubtToken &&
            outbox.ProcessingStartedAt is DateTime inDoubtStartedAt)
        {
            await using var recoveryScope = scopeFactory.CreateAsyncScope();
            var recoveryRepository = recoveryScope.ServiceProvider.GetRequiredService<IIntegrationSyncOutboxRepository>();
            bool parked = await recoveryRepository.ParkAmbiguousAsync(
                new IntegrationSyncClaimIdentity(outbox.TenantId, outbox.Id, inDoubtToken, inDoubtStartedAt),
                DateTime.UtcNow,
                cancellationToken);
            return new IntegrationSyncSingleDrainResult(
                parked ? IntegrationSyncDrainOutcome.Ambiguous : IntegrationSyncDrainOutcome.AlreadyClaimed,
                outbox.Id);
        }

        var startedAt = DateTime.UtcNow;
        var leaseToken = Guid.CreateVersion7();
        var claim = new IntegrationSyncClaimIdentity(outbox.TenantId, outbox.Id, leaseToken, startedAt);

        {
            await using var claimScope = scopeFactory.CreateAsyncScope();
            var claimRepository = claimScope.ServiceProvider.GetRequiredService<IIntegrationSyncOutboxRepository>();
            var claimed = await claimRepository.TryClaimAsync(
                new IntegrationSyncClaimRequest(
                    outbox.TenantId,
                    outbox.Id,
                    leaseToken,
                    startedAt,
                    startedAt.AddSeconds(-_settings.ProcessingLeaseTimeoutSeconds)),
                cancellationToken);
            if (!claimed)
            {
                return new IntegrationSyncSingleDrainResult(IntegrationSyncDrainOutcome.AlreadyClaimed, outbox.Id);
            }
        }

        await using var executionScope = scopeFactory.CreateAsyncScope();
        var repository = executionScope.ServiceProvider.GetRequiredService<IIntegrationSyncOutboxRepository>();
        var activeClaim = await repository.GetActiveClaimAsync(claim, cancellationToken);
        if (activeClaim is null)
        {
            return new IntegrationSyncSingleDrainResult(IntegrationSyncDrainOutcome.AlreadyClaimed, outbox.Id);
        }

        var tenantAccessor = executionScope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        Guid? previousTenantId = tenantAccessor.TenantId;
        tenantAccessor.SetTenant(activeClaim.TenantId);
        try
        {
            if (activeClaim.UserId is not Guid userId)
            {
                bool failed = await repository.FailAsync(
                    claim,
                    "Integration sync has no durable user identity.",
                    false,
                    TimeSpan.Zero,
                    _settings.MaxAttemptCount,
                    DateTime.UtcNow,
                    cancellationToken);
                return new IntegrationSyncSingleDrainResult(
                    failed ? IntegrationSyncDrainOutcome.DeadLettered : IntegrationSyncDrainOutcome.AlreadyClaimed,
                    activeClaim.Id);
            }

            var privacyErasureStateRepository = executionScope.ServiceProvider.GetRequiredService<IPrivacyErasureStateRepository>();
            if (await privacyErasureStateRepository.GetBySubjectAsync(userId, cancellationToken) is not null)
            {
                bool failed = await repository.FailAsync(
                    claim,
                    PrivacyErasureFencedMessage,
                    false,
                    TimeSpan.Zero,
                    _settings.MaxAttemptCount,
                    DateTime.UtcNow,
                    cancellationToken);
                return new IntegrationSyncSingleDrainResult(
                    failed ? IntegrationSyncDrainOutcome.DeadLettered : IntegrationSyncDrainOutcome.AlreadyClaimed,
                    activeClaim.Id);
            }

            var listmonkSyncService = executionScope.ServiceProvider.GetRequiredService<ListmonkSyncService>();
            var syncResult = await listmonkSyncService.SyncSubscriberAsync(
                activeClaim,
                ct => repository.MarkProviderHandoffStartedAsync(claim, DateTime.UtcNow, ct),
                cancellationToken);
            if (syncResult.Outcome == ListmonkSyncOutcome.LostClaim)
            {
                return new IntegrationSyncSingleDrainResult(IntegrationSyncDrainOutcome.AlreadyClaimed, activeClaim.Id);
            }

            if (syncResult.Succeeded)
            {
                bool completed = await repository.CompleteAsync(claim, DateTime.UtcNow, cancellationToken);
                return new IntegrationSyncSingleDrainResult(
                    completed ? IntegrationSyncDrainOutcome.Completed : IntegrationSyncDrainOutcome.AlreadyClaimed,
                    activeClaim.Id);
            }

            if (syncResult.Outcome == ListmonkSyncOutcome.Ambiguous)
            {
                bool parked = await repository.ParkAmbiguousAsync(claim, DateTime.UtcNow, cancellationToken);
                return new IntegrationSyncSingleDrainResult(
                    parked ? IntegrationSyncDrainOutcome.Ambiguous : IntegrationSyncDrainOutcome.AlreadyClaimed,
                    activeClaim.Id);
            }

            var failedAttemptCount = activeClaim.AttemptCount;
            var maxAttempts = Math.Min(activeClaim.MaxAttempts, _settings.MaxAttemptCount);
            var willDeadLetter = !syncResult.IsRetryable || failedAttemptCount >= maxAttempts;
            var retryDelay = TimeSpan.FromSeconds(_settings.CalculateRetryDelay(failedAttemptCount));

            bool settled = await repository.FailAsync(
                claim,
                syncResult.ErrorMessage ?? "Listmonk sync failed.",
                syncResult.IsRetryable,
                retryDelay,
                _settings.MaxAttemptCount,
                DateTime.UtcNow,
                cancellationToken);

            return new IntegrationSyncSingleDrainResult(
                !settled
                    ? IntegrationSyncDrainOutcome.AlreadyClaimed
                    : willDeadLetter ? IntegrationSyncDrainOutcome.DeadLettered : IntegrationSyncDrainOutcome.RetryScheduled,
                activeClaim.Id);
        }
        finally
        {
            if (previousTenantId is { } previous)
            {
                tenantAccessor.SetTenant(previous);
            }
            else
            {
                tenantAccessor.Clear();
            }
        }
    }
}
