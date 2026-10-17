using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.Features.RegistrationSubmissions.Commands;
using Explore.Application.Telemetry;
using Explore.Domain;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Explore.Application.Services.Registration;

public sealed class RegistrationProviderSubscriptionLifecycleService(
    IRegistrationProviderSubscriptionCustodyRepository stateRepository,
    IRegistrationProviderRepository providerRepository,
    IRegistrationProviderRegistry providerRegistry,
    IRegistrationProviderCallbackUriBuilder callbackUriBuilder,
    IIncomingWebhookMessageRepository messageRepository,
    IIncomingWebhookEffectOutboxRepository pointerRepository,
    IRegistrationProviderCallbackReceiptProtector receiptProtector,
    IUnitOfWork unitOfWork,
    BusinessMetrics metrics,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan SweepOverlap = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PeriodicSweepInterval = TimeSpan.FromHours(6);

    public async Task<bool> ProcessAsync(
        RegistrationProviderSubscriptionClaim claim,
        RegistrationProviderSubscriptionOperation operation,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(operation))
            throw new ArgumentOutOfRangeException(nameof(operation));
        try
        {
            var state = await stateRepository.GetActiveClaimAsync(claim, cancellationToken);
            if (state is null) return false;
            var binding = await providerRepository.GetBindingAsync(
                claim.TenantId, claim.RegistrationProviderBindingId, cancellationToken);
            await RequireCurrentAsync(claim, binding, cancellationToken);
            return operation == RegistrationProviderSubscriptionOperation.Renewal
                ? await ProcessRenewalAsync(claim, binding, cancellationToken)
                : await ProcessSweepAsync(claim, state, binding, cancellationToken);
        }
        catch (RegistrationProviderSubscriptionClaimLostException)
        {
            return false;
        }
    }

    private async Task<bool> ProcessRenewalAsync(
        RegistrationProviderSubscriptionClaim claim,
        RegistrationProviderBinding? binding,
        CancellationToken cancellationToken)
    {
        if (binding?.Connection is null)
            return await FailAsync(claim, binding, RegistrationProviderSubscriptionOperation.Renewal, "binding_missing", cancellationToken);
        RegistrationProviderTuple tuple = Tuple(binding.Connection);
        if (providerRegistry.TryResolve(tuple) is not IRegistrationProviderSubscriptionManager manager)
            return await FailAsync(claim, binding, RegistrationProviderSubscriptionOperation.Renewal, "subscription_unsupported", cancellationToken);

        var request = new RegistrationProviderSubscriptionRequest(claim.TenantId, binding, binding.Connection,
            tuple, callbackUriBuilder.Build(binding.Connection.ProviderCode, binding.Id));
        // Commit ambiguity before network entry. No provider call participates in a
        // retryable local transaction, and an expired claim cannot clear this marker.
        await MutateAsync(claim, binding, (state, now) =>
        {
            state.MarkRenewalAttempt(claim.LeaseToken, claim.ProcessingGeneration, now);
            state.BeginRenewalHandoff(claim.LeaseToken, claim.ProcessingGeneration, now);
        }, cancellationToken);
        try
        {
            await RequireCurrentAsync(claim, binding, cancellationToken);
            RegistrationProviderSubscriptionResult result = await manager.EnsureSubscriptionAsync(request, cancellationToken);
            await RequireCurrentAsync(claim, binding, cancellationToken);
            await MutateAsync(claim, binding, (state, now) =>
            {
                if (!result.IsActive)
                    state.RejectRenewal(claim.LeaseToken, claim.ProcessingGeneration, now);
                else
                    state.MarkRenewalSuccess(claim.LeaseToken, claim.ProcessingGeneration,
                        result.ProviderSubscriptionId ?? state.WatchId,
                        DateTime.SpecifyKind(result.ExpiresAtUtc ?? now.AddDays(6), DateTimeKind.Utc), now);
            }, cancellationToken);
            metrics.RecordRegistrationProviderSubscriptionOperation("renewal", result.IsActive ? "success" : "rejected");
            return true;
        }
        catch (Exception exception) when (exception is not RegistrationProviderSubscriptionClaimLostException &&
            !cancellationToken.IsCancellationRequested)
        {
            await MutateAsync(claim, binding, (state, now) =>
                state.ParkRenewalInDoubt(claim.LeaseToken, claim.ProcessingGeneration, now), cancellationToken);
            metrics.RecordRegistrationProviderSubscriptionOperation("renewal", "in_doubt");
            return true;
        }
    }

    private async Task<bool> ProcessSweepAsync(
        RegistrationProviderSubscriptionClaim claim,
        RegistrationProviderSubscriptionState state,
        RegistrationProviderBinding? binding,
        CancellationToken cancellationToken)
    {
        if (binding?.Connection is null || providerRegistry.TryResolve(Tuple(binding.Connection)) is not IRegistrationProviderReconciliationProvider reconciler)
            return await FailAsync(claim, binding, RegistrationProviderSubscriptionOperation.Sweep, "sweep_unsupported", cancellationToken);
        try
        {
            string? continuationCursor = IsContinuationCursor(state.ResponseCheckpoint) ? state.ResponseCheckpoint : null;
            DateTime since = continuationCursor is null && DateTime.TryParse(state.ResponseCheckpoint, out DateTime checkpoint)
                ? DateTime.SpecifyKind(checkpoint, DateTimeKind.Utc).Subtract(SweepOverlap)
                : timeProvider.GetUtcNow().UtcDateTime.Subtract(SweepOverlap);
            RegistrationProviderTuple tuple = Tuple(binding.Connection);
            var request = new RegistrationProviderReconciliationRequest(claim.TenantId, binding, binding.Connection, tuple, since, continuationCursor);
            await RequireCurrentAsync(claim, binding, cancellationToken);
            RegistrationProviderReconciliationResult result = await reconciler.ReconcileAsync(request, cancellationToken);
            await RequireCurrentAsync(claim, binding, cancellationToken);
            if (result.ObservedSubmissionCount > (result.Responses?.Count ?? 0) || result.HasMore && string.IsNullOrWhiteSpace(result.ContinuationCursor))
            {
                throw new InvalidOperationException("Registration provider reconciliation returned responses that cannot be durably queued without losing checkpoint safety.");
            }

            await unitOfWork.ExecuteReadCommittedAsync(async ct =>
            {
                await stateRepository.FenceAsync(claim, binding, ct);
                await QueueResponsesAsync(claim, binding, binding.Connection, tuple, result.Responses ?? [], ct);
                var current = await stateRepository.FenceAsync(claim, binding, ct);
                await RequireCurrentAsync(claim, binding, ct);
                DateTime now = timeProvider.GetUtcNow().UtcDateTime;
                string nextCheckpoint = result.HasMore ? result.ContinuationCursor!
                    : result.NextCheckpoint ?? now.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
                current.SettleCheckpoint(claim.LeaseToken, claim.ProcessingGeneration, nextCheckpoint,
                    result.HasMore ? now : now.Add(PeriodicSweepInterval), now);
                await stateRepository.SaveChangesAsync(ct);
                RequireUnexpired(claim);
                return true;
            }, cancellationToken);
            metrics.RecordRegistrationProviderSubscriptionOperation("sweep", "success");
            return true;
        }
        catch (Exception exception) when (exception is not RegistrationProviderSubscriptionClaimLostException &&
            !cancellationToken.IsCancellationRequested)
        {
            return await FailAsync(claim, binding, RegistrationProviderSubscriptionOperation.Sweep, "sweep_failed", cancellationToken);
        }
    }

    private async Task MutateAsync(
        RegistrationProviderSubscriptionClaim claim, RegistrationProviderBinding? binding,
        Action<RegistrationProviderSubscriptionState, DateTime> mutation, CancellationToken cancellationToken)
    {
        await unitOfWork.ExecuteReadCommittedAsync(async ct =>
        {
            var state = await stateRepository.FenceAsync(claim, binding, ct);
            await RequireCurrentAsync(claim, binding, ct);
            mutation(state, timeProvider.GetUtcNow().UtcDateTime);
            await stateRepository.SaveChangesAsync(ct);
            RequireUnexpired(claim);
            return true;
        }, cancellationToken);
    }

    private async Task<bool> FailAsync(RegistrationProviderSubscriptionClaim claim, RegistrationProviderBinding? binding,
        RegistrationProviderSubscriptionOperation operation, string category, CancellationToken cancellationToken)
    {
        await MutateAsync(claim, binding, (state, now) => state.Fail(operation, claim.LeaseToken,
            claim.ProcessingGeneration, category, NextBackoff(now,
                operation == RegistrationProviderSubscriptionOperation.Renewal ? state.RenewalFailureCount : state.SweepFailureCount), now),
            cancellationToken);
        metrics.RecordRegistrationProviderSubscriptionOperation(
            operation == RegistrationProviderSubscriptionOperation.Renewal ? "renewal" : "sweep",
            category switch
            {
                "binding_missing" => "missing_binding",
                "subscription_unsupported" or "sweep_unsupported" => "unsupported",
                _ => "failure"
            });
        return true;
    }

    private async Task RequireCurrentAsync(RegistrationProviderSubscriptionClaim claim,
        RegistrationProviderBinding? binding, CancellationToken cancellationToken)
    {
        if (!await stateRepository.IsCurrentAsync(claim, binding, cancellationToken))
            throw new RegistrationProviderSubscriptionClaimLostException();
        RequireUnexpired(claim);
    }

    private void RequireUnexpired(RegistrationProviderSubscriptionClaim claim)
    {
        if (claim.LeaseExpiresAt <= timeProvider.GetUtcNow().UtcDateTime)
            throw new RegistrationProviderSubscriptionClaimLostException();
    }

    private static RegistrationProviderTuple Tuple(RegistrationProviderConnection connection) => new(
        connection.ProviderCode,
        connection.ProviderDeploymentCode,
        connection.ApiVersion,
        connection.AdapterPolicyVersion,
        connection.ConformanceEvidenceRevision);

    private static DateTime NextBackoff(DateTime now, int failures) => now.Add(TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Min(5, failures)))));

    private static bool IsContinuationCursor(string? value) => value?.StartsWith("registration-provider-cursor:", StringComparison.Ordinal) == true;

    private async Task QueueResponsesAsync(
        RegistrationProviderSubscriptionClaim claim,
        RegistrationProviderBinding binding,
        RegistrationProviderConnection connection,
        RegistrationProviderTuple tuple,
        IReadOnlyList<RegistrationProviderReconciledSubmission> responses,
        CancellationToken cancellationToken)
    {
        Guid tenantId = claim.TenantId;
        foreach (RegistrationProviderReconciledSubmission response in responses)
        {
            await RequireCurrentAsync(claim, binding, cancellationToken);
            string providerDecisionId = $"{binding.Id:N}:{response.ProviderSubmissionId}";
            if (await pointerRepository.GetByProviderIdentityAsync(tenantId, "registration-provider", providerDecisionId, ProcessProviderSubmissionEffectCommandHandler.StableEffectKind, cancellationToken) is not null)
            {
                continue;
            }

            await RequireCurrentAsync(claim, binding, cancellationToken);
            DateTime now = timeProvider.GetUtcNow().UtcDateTime;
            byte[] payload = Encoding.UTF8.GetBytes("{}");
            string payloadHash = "sha256:" + Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
            string receipt = receiptProtector.Protect(new RegistrationProviderCallbackReceipt(
                tenantId,
                connection.Id,
                binding.Id,
                connection.ProviderCode,
                tuple.Key,
                payloadHash,
                response.ProviderSubmissionId,
                now,
                Guid.CreateVersion7().ToString("N")));
            string headers = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["X-Registration-Callback-Provider"] = connection.ProviderCode,
                ["X-Registration-Verification-Receipt"] = receipt
            });
            IncomingWebhookMessage message = IncomingWebhookMessage.CreateVerified(
                tenantId,
                "registration-provider",
                providerDecisionId,
                providerDecisionId,
                ProcessProviderSubmissionEffectCommandHandler.StableEffectKind,
                payload,
                payloadHash,
                "application/json",
                "utf-8",
                headers,
                now,
                now,
                now.AddDays(14),
                "registration-provider-sweep-v1",
                now.AddDays(30),
                now.AddDays(90),
                now.AddDays(14),
                now.AddDays(30),
                binding.Id);
            await RequireCurrentAsync(claim, binding, cancellationToken);
            await messageRepository.TryCreateAsync(message, cancellationToken);
            await RequireCurrentAsync(claim, binding, cancellationToken);
            await pointerRepository.AddAsync(IncomingWebhookEffectOutbox.CreatePending(
                tenantId,
                message.Id,
                "registration-provider",
                providerDecisionId,
                ProcessProviderSubmissionEffectCommandHandler.StableEffectKind,
                payloadHash,
                now), cancellationToken);
            await RequireCurrentAsync(claim, binding, cancellationToken);
            await pointerRepository.SaveChangesAsync(cancellationToken);
            await RequireCurrentAsync(claim, binding, cancellationToken);
        }
    }
}
