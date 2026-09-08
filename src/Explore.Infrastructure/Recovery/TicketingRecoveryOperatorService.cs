using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Recovery;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Contracts.Secrets;
using Explore.Domain;
using Explore.Domain.Secrets;
using Explore.Secrets.Configuration;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Recovery;

public enum TicketingRecoveryOperatorAction
{
    StopSales = 1,
    PauseWorkers = 2,
    Reconcile = 3,
    ResolveUnknown = 4,
    ReopenWorkers = 5,
    ReopenSales = 6,
    DeadLetter = 7,
}

public sealed class TicketingRecoveryOperatorService(
    ITicketingRecoveryOperatorStore store,
    ISchedulerOperations scheduler,
    ISecretResolver secretResolver,
    IOptions<TicketingRecoveryOperatorOptions> options,
    TimeProvider timeProvider)
{
    public async Task<TicketingRecoveryCheckpoint?> BeginRecoveryAsync(
        TicketingRecoveryManifest manifest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!options.Value.Enabled)
        {
            return null;
        }

        SecretResolutionResult secret = await secretResolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Ticketing.RecoveryManifestHmacKey,
            tenantId: null,
            cancellationToken);
        if (!secret.IsResolved ||
            !VerifyManifest(manifest, secret.Value))
        {
            return null;
        }

        return await store.BeginRecoveryAsync(
            manifest,
            UtcNow(),
            cancellationToken);
    }

    public Task<bool> StopSalesAsync(
        Guid tenantId,
        Guid recoveryOperationId,
        long nextWorkerFence,
        CancellationToken cancellationToken) =>
        options.Value.Enabled
            ? store.StopSalesAsync(
                tenantId,
                recoveryOperationId,
                nextWorkerFence,
                UtcNow(),
                cancellationToken)
            : Task.FromResult(false);

    public async Task<bool> PauseWorkersAsync(
        Guid tenantId,
        Guid recoveryOperationId,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return false;
        }

        SchedulerOperationResult schedulerResult =
            await scheduler.PauseAllAsync(cancellationToken);
        if (schedulerResult.Outcome != SchedulerOperationOutcome.Succeeded)
        {
            return false;
        }

        return await store.PauseWorkersAsync(
            tenantId,
            recoveryOperationId,
            UtcNow(),
            cancellationToken);
    }

    public async Task<TicketingRecoveryCheckpoint?> ReconcileAsync(
        Guid tenantId,
        Guid recoveryOperationId,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return null;
        }

        TicketingRecoveryCheckpoint? current =
            await store.GetAsync(
                tenantId,
                recoveryOperationId,
                cancellationToken);
        if (current is null)
        {
            return null;
        }

        TicketingRecoveryOperatorOptions configured = options.Value;
        return await store.ValidateAndRotateAsync(
            tenantId,
            recoveryOperationId,
            configured.ExpectedReleaseRevision,
            configured.ExpectedSchemaRevision,
            configured.MinimumRetainedKeyVersion,
            configured.MinimumAuthorityFloor,
            configured.MinimumProviderCursor,
            configured.MinimumIdempotencyFloor,
            configured.MinimumWorkerFence,
            checked(current.CapabilityGeneration + 1),
            checked(current.CredentialGeneration + 1),
            checked(Math.Max(
                current.WorkerFence,
                configured.MinimumWorkerFence) + 1),
            UtcNow(),
            cancellationToken);
    }

    public Task<bool> ResolveUnknownAsync(
        Guid tenantId,
        Guid effectId,
        long expectedFence,
        bool retry,
        CancellationToken cancellationToken) =>
        options.Value.Enabled
            ? store.ResolveUnknownAsync(
                tenantId,
                effectId,
                expectedFence,
                retry,
                UtcNow(),
                cancellationToken)
            : Task.FromResult(false);

    public Task<bool> DeadLetterAsync(
        Guid tenantId,
        Guid effectId,
        long expectedFence,
        CancellationToken cancellationToken) =>
        options.Value.Enabled
            ? store.DeadLetterAsync(
                tenantId,
                effectId,
                expectedFence,
                UtcNow(),
                cancellationToken)
            : Task.FromResult(false);

    public async Task<bool> ReopenWorkersAsync(
        Guid tenantId,
        Guid recoveryOperationId,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return false;
        }

        TicketingRecoveryCheckpoint? checkpoint =
            await store.GetAsync(
                tenantId,
                recoveryOperationId,
                cancellationToken);
        if (checkpoint is null ||
            checkpoint.Status is not (
                TicketingRecoveryStatus.AuthorityRotated or
                TicketingRecoveryStatus.WorkersOpen))
        {
            return false;
        }

        if (checkpoint.Status ==
                TicketingRecoveryStatus.AuthorityRotated &&
            !await store.OpenWorkersAsync(
                tenantId,
                recoveryOperationId,
                checkpoint.WorkerFence,
                UtcNow(),
                cancellationToken))
        {
            return false;
        }

        SchedulerOperationResult schedulerResult =
            await scheduler.ResumeAllAsync(cancellationToken);
        return schedulerResult.Outcome == SchedulerOperationOutcome.Succeeded;
    }

    public Task<bool> ReopenSalesAsync(
        Guid tenantId,
        Guid recoveryOperationId,
        CancellationToken cancellationToken) =>
        options.Value.Enabled
            ? store.OpenSalesAsync(
                tenantId,
                recoveryOperationId,
                UtcNow(),
                cancellationToken)
            : Task.FromResult(false);

    private DateTime UtcNow() =>
        timeProvider.GetUtcNow().UtcDateTime;

    private static bool VerifyManifest(
        TicketingRecoveryManifest manifest,
        string secret)
    {
        byte[] key = Encoding.UTF8.GetBytes(secret);
        byte[] payload = Encoding.UTF8.GetBytes(string.Join(
            '\n',
            manifest.OperationId.ToString("D", CultureInfo.InvariantCulture),
            manifest.TenantId.ToString("D", CultureInfo.InvariantCulture),
            manifest.ReleaseRevision,
            manifest.SchemaRevision,
            manifest.DatabaseCheckpoint.ToString(CultureInfo.InvariantCulture),
            manifest.ObjectCutoffUtc.ToString("O", CultureInfo.InvariantCulture),
            manifest.RetainedKeyVersion.ToString(CultureInfo.InvariantCulture),
            manifest.AuthorityFloor.ToString(CultureInfo.InvariantCulture),
            manifest.ProviderCursor.ToString(CultureInfo.InvariantCulture),
            manifest.IdempotencyFloor.ToString(CultureInfo.InvariantCulture),
            manifest.WorkerFence.ToString(CultureInfo.InvariantCulture),
            manifest.CapabilityGeneration.ToString(CultureInfo.InvariantCulture),
            manifest.CredentialGeneration.ToString(CultureInfo.InvariantCulture)));
        byte[] expected = Convert.FromHexString(manifest.Digest);
        byte[] actual = HMACSHA256.HashData(key, payload);
        bool valid = CryptographicOperations.FixedTimeEquals(
            actual,
            expected);
        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(actual);
        CryptographicOperations.ZeroMemory(expected);
        return valid;
    }
}
