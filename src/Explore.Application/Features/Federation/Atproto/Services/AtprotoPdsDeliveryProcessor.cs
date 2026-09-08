using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Features.Federation.Atproto.Services;

public enum AtprotoPdsClaimOutcome
{
    ClaimLost,
    GateDenied,
    Delivered,
    DeliveryFailed
}

public enum AtprotoPdsFailureDisposition
{
    None,
    RetryScheduled,
    DeadLettered
}

public sealed record AtprotoPdsClaimResult(
    AtprotoPdsClaimOutcome Outcome,
    string? FailureCode,
    AtprotoPdsFailureDisposition FailureDisposition);

public sealed class AtprotoPdsDeliveryProcessor(
    IPdsSyncOutboxRepository outboxRepository,
    IAtprotoDeliveryGate deliveryGate,
    IAtprotoPdsDeliveryGateway deliveryGateway,
    TimeProvider timeProvider)
{
    public async Task<AtprotoPdsClaimResult> ProcessAsync(
        PdsSyncClaim claim,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        var observedAt = UtcNow();
        var outbox = await outboxRepository.GetActiveClaimAsync(
            claim,
            observedAt,
            cancellationToken);
        if (outbox is null)
        {
            return Result(AtprotoPdsClaimOutcome.ClaimLost);
        }

        AtprotoDeliveryGateResult firstGate = await deliveryGate.CheckDeliveryAsync(
            outbox,
            new DateTimeOffset(observedAt),
            cancellationToken);
        if (!firstGate.Allowed)
        {
            return await FailAsync(
                claim,
                firstGate.ReasonCode ?? "delivery_denied",
                retryable: false,
                outbox.RetryCount,
                outbox.MaxRetries,
                cancellationToken);
        }

        PdsSyncCompensationEvidence? compensationEvidence = outbox.ExpectedCid is null
            && outbox.Operation is Explore.Domain.Federation.PdsSyncOperation.Update
                or Explore.Domain.Federation.PdsSyncOperation.Delete
            ? await outboxRepository.GetCompensationEvidenceAsync(outbox, cancellationToken)
            : null;
        if (compensationEvidence is { IsComplete: false }
            || compensationEvidence is
            {
                AllowedPayloads.Count: 0,
                AllowedBaseCids.Count: 0
            })
        {
            return await FailAsync(
                claim,
                "record_conflict",
                retryable: false,
                outbox.RetryCount,
                outbox.MaxRetries,
                cancellationToken);
        }

        observedAt = UtcNow();
        var renewed = await outboxRepository.TryRenewClaimAsync(
            claim,
            observedAt,
            observedAt.Add(leaseDuration),
            cancellationToken);
        if (!renewed)
        {
            return Result(AtprotoPdsClaimOutcome.ClaimLost);
        }

        AtprotoDeliveryGateResult finalGate = await deliveryGate.CheckDeliveryAsync(
            outbox,
            new DateTimeOffset(UtcNow()),
            cancellationToken);
        if (!finalGate.Allowed)
        {
            return await FailAsync(
                claim,
                finalGate.ReasonCode ?? "delivery_denied",
                retryable: false,
                outbox.RetryCount,
                outbox.MaxRetries,
                cancellationToken);
        }

        if (!Uri.TryCreate(outbox.PdsHost, UriKind.Absolute, out var pdsHost))
        {
            return await FailAsync(
                claim,
                "pds_host_invalid",
                retryable: false,
                outbox.RetryCount,
                outbox.MaxRetries,
                cancellationToken);
        }

        AtprotoPdsDeliveryResult result;
        using var remoteCallCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        remoteCallCts.CancelAfter(RemoteCallTimeout(leaseDuration));
        try
        {
            result = await deliveryGateway.DeliverAsync(
                new AtprotoPdsDeliveryRequest(
                    outbox.TenantId,
                    outbox.UserId,
                    outbox.Did,
                    pdsHost,
                    outbox.Collection,
                    outbox.RecordKey,
                    outbox.Operation,
                    outbox.Payload,
                    outbox.ExpectedCid,
                    compensationEvidence?.AllowedPayloads,
                    compensationEvidence?.AllowedBaseCids,
                    compensationEvidence?.IsComplete ?? true),
                remoteCallCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return await FailAsync(
                claim,
                "provider_timeout",
                retryable: true,
                outbox.RetryCount,
                outbox.MaxRetries,
                cancellationToken);
        }
        catch
        {
            return await FailAsync(
                claim,
                "delivery_unexpected",
                retryable: true,
                outbox.RetryCount,
                outbox.MaxRetries,
                cancellationToken);
        }

        if (!result.Succeeded)
        {
            return await FailAsync(
                claim,
                result.FailureCode ?? "delivery_failed",
                result.Retryable,
                outbox.RetryCount,
                outbox.MaxRetries,
                cancellationToken);
        }

        observedAt = UtcNow();
        renewed = await outboxRepository.TryRenewClaimAsync(
            claim,
            observedAt,
            observedAt.Add(leaseDuration),
            cancellationToken);
        if (!renewed)
        {
            return Result(AtprotoPdsClaimOutcome.ClaimLost);
        }

        DateTime settledAt = UtcNow();
        var settled = await outboxRepository.TrySettleAsync(
            claim,
            result.Uri,
            result.Cid,
            settledAt,
            cancellationToken,
            result.ObservedBaseCid);
        return Result(settled ? AtprotoPdsClaimOutcome.Delivered : AtprotoPdsClaimOutcome.ClaimLost);
    }

    private async Task<AtprotoPdsClaimResult> FailAsync(
        PdsSyncClaim claim,
        string failureCode,
        bool retryable,
        int retryCount,
        int maxRetries,
        CancellationToken cancellationToken)
    {
        var failed = await outboxRepository.TryFailAsync(
            claim,
            failureCode,
            retryable,
            UtcNow(),
            RetryDelay(retryCount),
            cancellationToken);
        if (!failed)
        {
            return Result(AtprotoPdsClaimOutcome.ClaimLost);
        }

        AtprotoPdsFailureDisposition disposition = retryable && retryCount + 1 < maxRetries
            ? AtprotoPdsFailureDisposition.RetryScheduled
            : AtprotoPdsFailureDisposition.DeadLettered;
        return new(AtprotoPdsClaimOutcome.DeliveryFailed, failureCode, disposition);
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static TimeSpan RetryDelay(int retryCount) =>
        TimeSpan.FromSeconds(Math.Min(Math.Pow(2, Math.Clamp(retryCount + 1, 1, 12)), 3600));

    private static TimeSpan RemoteCallTimeout(TimeSpan leaseDuration)
    {
        TimeSpan leaseBound = leaseDuration > TimeSpan.FromSeconds(5)
            ? leaseDuration - TimeSpan.FromSeconds(5)
            : TimeSpan.FromTicks(Math.Max(1, leaseDuration.Ticks / 2));
        return leaseBound < TimeSpan.FromSeconds(60) ? leaseBound : TimeSpan.FromSeconds(60);
    }

    private static AtprotoPdsClaimResult Result(AtprotoPdsClaimOutcome outcome) =>
        new(outcome, null, AtprotoPdsFailureDisposition.None);
}
