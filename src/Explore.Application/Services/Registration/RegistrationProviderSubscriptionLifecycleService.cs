using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.Features.RegistrationSubmissions.Commands;
using Explore.Application.Services.Webhooks;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Explore.Application.Services.Registration;

public sealed class RegistrationProviderSubscriptionLifecycleService(
    IRegistrationProviderSubscriptionStateRepository stateRepository,
    IRegistrationProviderRepository providerRepository,
    IRegistrationProviderRegistry providerRegistry,
    ITenantContextAccessor tenantContextAccessor,
    IRegistrationProviderCallbackUriBuilder callbackUriBuilder,
    IIncomingWebhookMessageRepository messageRepository,
    IIncomingWebhookEffectOutboxRepository pointerRepository,
    IRegistrationProviderCallbackReceiptProtector receiptProtector,
    IUnitOfWork unitOfWork,
    BusinessMetrics metrics,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RenewalSafetyMargin = TimeSpan.FromDays(2);
    private static readonly TimeSpan SweepOverlap = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PeriodicSweepInterval = TimeSpan.FromHours(6);
    private const int BatchSize = 10;

    public async Task<int> DrainOnceAsync(CancellationToken cancellationToken)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        int processed = 0;
        await RecordWatchCountsAsync(now, cancellationToken);
        IReadOnlyList<RegistrationProviderSubscriptionState> renewals = await stateRepository.ClaimDueRenewalsAsync(
            BatchSize,
            now.Add(RenewalSafetyMargin),
            now,
            LeaseDuration,
            cancellationToken);
        foreach (RegistrationProviderSubscriptionState state in renewals)
        {
            await ProcessInTenantAsync(state.TenantId, () => ProcessRenewalAsync(state, cancellationToken));
            processed++;
        }

        IReadOnlyList<RegistrationProviderSubscriptionState> sweeps = await stateRepository.ClaimDueSweepsAsync(
            BatchSize,
            now,
            LeaseDuration,
            cancellationToken);
        foreach (RegistrationProviderSubscriptionState state in sweeps)
        {
            await ProcessInTenantAsync(state.TenantId, () => ProcessSweepAsync(state, cancellationToken));
            processed++;
        }

        return processed;
    }

    private async Task ProcessRenewalAsync(RegistrationProviderSubscriptionState state, CancellationToken cancellationToken)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        if (state.LeaseToken is not { } leaseToken) return;
        long generation = state.ProcessingGeneration;
        try
        {
            state.MarkRenewalAttempt(leaseToken, generation, now);
            await stateRepository.SaveChangesAsync(cancellationToken);

            RegistrationProviderBinding? binding = await providerRepository.GetBindingAsync(state.TenantId, state.RegistrationProviderBindingId, cancellationToken);
            if (binding?.Connection is null)
            {
                state.Fail(RegistrationProviderSubscriptionOperation.Renewal, leaseToken, generation, "binding_missing", NextBackoff(now, state.RenewalFailureCount), now);
                await stateRepository.SaveChangesAsync(cancellationToken);
                metrics.RecordRegistrationProviderSubscriptionOperation("renewal", "missing_binding");
                return;
            }

            RegistrationProviderTuple tuple = Tuple(binding.Connection);
            if (providerRegistry.TryResolve(tuple) is not IRegistrationProviderSubscriptionManager manager)
            {
                state.Fail(RegistrationProviderSubscriptionOperation.Renewal, leaseToken, generation, "subscription_unsupported", NextBackoff(now, state.RenewalFailureCount), now);
                await stateRepository.SaveChangesAsync(cancellationToken);
                metrics.RecordRegistrationProviderSubscriptionOperation("renewal", "unsupported");
                return;
            }

            state.BeginRenewalHandoff(leaseToken, generation, now);
            await stateRepository.SaveChangesAsync(cancellationToken);

            RegistrationProviderSubscriptionResult result = await manager.EnsureSubscriptionAsync(new(
                state.TenantId,
                binding,
                binding.Connection,
                tuple,
                callbackUriBuilder.Build(binding.Connection.ProviderCode, binding.Id)), cancellationToken);
            if (!result.IsActive)
            {
                state.RejectRenewal(leaseToken, generation, now);
                await stateRepository.SaveChangesAsync(cancellationToken);
                metrics.RecordRegistrationProviderSubscriptionOperation("renewal", "rejected");
                return;
            }

            DateTime expiresAt = DateTime.SpecifyKind(result.ExpiresAtUtc ?? now.AddDays(6), DateTimeKind.Utc);
            state.MarkRenewalSuccess(leaseToken, generation, result.ProviderSubscriptionId ?? state.WatchId, expiresAt, now);
            await stateRepository.SaveChangesAsync(cancellationToken);
            metrics.RecordRegistrationProviderSubscriptionOperation("renewal", "success");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            state.ParkRenewalInDoubt(leaseToken, generation, now);
            await stateRepository.SaveChangesAsync(cancellationToken);
            metrics.RecordRegistrationProviderSubscriptionOperation("renewal", "in_doubt");
        }
    }

    private async Task ProcessSweepAsync(RegistrationProviderSubscriptionState state, CancellationToken cancellationToken)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        if (state.LeaseToken is not { } leaseToken) return;
        long generation = state.ProcessingGeneration;
        try
        {
            RegistrationProviderBinding? binding = await providerRepository.GetBindingAsync(state.TenantId, state.RegistrationProviderBindingId, cancellationToken);
            if (binding?.Connection is null || providerRegistry.TryResolve(Tuple(binding.Connection)) is not IRegistrationProviderReconciliationProvider reconciler)
            {
                state.Fail(RegistrationProviderSubscriptionOperation.Sweep, leaseToken, generation, "sweep_unsupported", NextBackoff(now, state.SweepFailureCount), now);
                await stateRepository.SaveChangesAsync(cancellationToken);
                metrics.RecordRegistrationProviderSubscriptionOperation("sweep", "unsupported");
                return;
            }

            string? continuationCursor = IsContinuationCursor(state.ResponseCheckpoint) ? state.ResponseCheckpoint : null;
            DateTime since = continuationCursor is null && DateTime.TryParse(state.ResponseCheckpoint, out DateTime checkpoint)
                ? DateTime.SpecifyKind(checkpoint, DateTimeKind.Utc).Subtract(SweepOverlap)
                : now.Subtract(SweepOverlap);
            RegistrationProviderTuple tuple = Tuple(binding.Connection);
            RegistrationProviderReconciliationResult result = await reconciler.ReconcileAsync(new(state.TenantId, binding, binding.Connection, tuple, since, continuationCursor), cancellationToken);
            if (result.ObservedSubmissionCount > (result.Responses?.Count ?? 0) || result.HasMore && string.IsNullOrWhiteSpace(result.ContinuationCursor))
            {
                throw new InvalidOperationException("Registration provider reconciliation returned responses that cannot be durably queued without losing checkpoint safety.");
            }

            await QueueResponsesAsync(state.TenantId, binding, binding.Connection, tuple, result.Responses ?? [], now, cancellationToken);
            string nextCheckpoint = result.HasMore
                ? result.ContinuationCursor!
                : result.NextCheckpoint ?? now.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            DateTime nextSweep = result.HasMore ? now : now.Add(PeriodicSweepInterval);
            state.SettleCheckpoint(leaseToken, generation, nextCheckpoint, nextSweep, now);
            await stateRepository.SaveChangesAsync(cancellationToken);
            metrics.RecordRegistrationProviderSubscriptionOperation("sweep", "success");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            state.Fail(RegistrationProviderSubscriptionOperation.Sweep, leaseToken, generation, "sweep_failed", NextBackoff(now, state.SweepFailureCount), now);
            await stateRepository.SaveChangesAsync(cancellationToken);
            metrics.RecordRegistrationProviderSubscriptionOperation("sweep", "failure");
        }
    }

    private async Task RecordWatchCountsAsync(DateTime now, CancellationToken cancellationToken)
    {
        IReadOnlyList<RegistrationProviderSubscriptionState> expiring = await stateRepository.GetExpiringAsync(now.Add(RenewalSafetyMargin), 1000, cancellationToken);
        IReadOnlyList<RegistrationProviderSubscriptionState> expired = await stateRepository.GetExpiringAsync(now, 1000, cancellationToken);
        metrics.RecordRegistrationProviderSubscriptionWatchCount("expiring", expiring.Count);
        metrics.RecordRegistrationProviderSubscriptionWatchCount("expired", expired.Count);
    }

    private static RegistrationProviderTuple Tuple(RegistrationProviderConnection connection) => new(
        connection.ProviderCode,
        connection.ProviderDeploymentCode,
        connection.ApiVersion,
        connection.AdapterPolicyVersion,
        connection.ConformanceEvidenceRevision);

    private static DateTime NextBackoff(DateTime now, int failures) => now.Add(TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Min(5, failures)))));

    private async Task ProcessInTenantAsync(Guid tenantId, Func<Task> operation)
    {
        Guid? previousTenantId = tenantContextAccessor.TenantId;
        tenantContextAccessor.SetTenant(tenantId);
        try
        {
            await operation();
        }
        finally
        {
            if (previousTenantId is { } previous)
            {
                tenantContextAccessor.SetTenant(previous);
            }
            else
            {
                tenantContextAccessor.Clear();
            }
        }
    }

    private static bool IsContinuationCursor(string? value) => value?.StartsWith("registration-provider-cursor:", StringComparison.Ordinal) == true;

    private async Task QueueResponsesAsync(
        Guid tenantId,
        RegistrationProviderBinding binding,
        RegistrationProviderConnection connection,
        RegistrationProviderTuple tuple,
        IReadOnlyList<RegistrationProviderReconciledSubmission> responses,
        DateTime now,
        CancellationToken cancellationToken)
    {
        foreach (RegistrationProviderReconciledSubmission response in responses)
        {
            await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                string providerDecisionId = $"{binding.Id:N}:{response.ProviderSubmissionId}";
                if (await pointerRepository.GetByProviderIdentityAsync(tenantId, "registration-provider", providerDecisionId, ProcessProviderSubmissionEffectCommandHandler.StableEffectKind, ct) is not null)
                {
                    return;
                }

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
                await messageRepository.TryCreateAsync(message, ct);
                await pointerRepository.AddAsync(IncomingWebhookEffectOutbox.CreatePending(
                    tenantId,
                    message.Id,
                    "registration-provider",
                    providerDecisionId,
                    ProcessProviderSubmissionEffectCommandHandler.StableEffectKind,
                    payloadHash,
                    now), ct);
                await pointerRepository.SaveChangesAsync(ct);
            }, cancellationToken);
        }
    }
}
