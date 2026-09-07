using Explore.Application.Contracts.Recovery;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class TicketingRecoveryRepository(
    ExploreDbContext dbContext) :
    ITicketingRecoveryOperatorStore
{
    public const string CanonicalFenceOrder =
        "recovery-checkpoint>capabilities>credentials>reissue-intents>queues>provider-cursors";

    public Task<TicketingRecoveryCheckpoint> BeginRecoveryAsync(
        TicketingRecoveryManifest manifest,
        DateTime createdAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return ExecuteSerializableAsync(async () =>
        {
            TicketingRecoveryCheckpoint? existing =
                await QueryCheckpoints()
                    .SingleOrDefaultAsync(value =>
                        value.TenantId == manifest.TenantId &&
                        value.RecoveryOperationId == manifest.OperationId,
                        cancellationToken);
            if (existing is not null)
            {
                if (!string.Equals(
                    existing.ManifestDigest,
                    manifest.Digest,
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Recovery operation replay does not match its manifest digest.");
                }

                return existing;
            }

            TicketingRecoveryCheckpoint checkpoint =
                TicketingRecoveryCheckpoint.Begin(manifest, createdAtUtc);
            dbContext.TicketingRecoveryCheckpoints.Add(checkpoint);
            await dbContext.SaveChangesAsync(cancellationToken);
            return checkpoint;
        }, cancellationToken);
    }

    public Task<TicketingRecoveryCheckpoint?> GetAsync(
        Guid tenantId,
        Guid recoveryOperationId,
        CancellationToken cancellationToken) =>
        QueryCheckpoints()
            .AsNoTracking()
            .SingleOrDefaultAsync(value =>
                value.TenantId == tenantId &&
                value.RecoveryOperationId == recoveryOperationId,
                cancellationToken);

    public Task<TicketingRecoveryCheckpoint?> ValidateAndRotateAsync(
        Guid tenantId,
        Guid recoveryOperationId,
        string runningReleaseRevision,
        string runningSchemaRevision,
        int minimumRetainedKeyVersion,
        long minimumAuthorityFloor,
        long minimumProviderCursor,
        long minimumIdempotencyFloor,
        long minimumWorkerFence,
        int nextCapabilityGeneration,
        int nextCredentialGeneration,
        long nextWorkerFence,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken) =>
        ExecuteSerializableAsync(async () =>
        {
            TicketingRecoveryCheckpoint? checkpoint =
                await QueryCheckpoints()
                    .SingleOrDefaultAsync(value =>
                        value.TenantId == tenantId &&
                        value.RecoveryOperationId == recoveryOperationId,
                        cancellationToken);
            if (checkpoint is null)
            {
                return null;
            }

            TicketingRecoveryValidationOutcome validation =
                checkpoint.Validate(
                    runningReleaseRevision,
                    runningSchemaRevision,
                    minimumRetainedKeyVersion,
                    minimumAuthorityFloor,
                    minimumProviderCursor,
                    minimumIdempotencyFloor,
                    minimumWorkerFence,
                    occurredAtUtc);
            if (validation != TicketingRecoveryValidationOutcome.Validated)
            {
                return checkpoint;
            }

            AdmissionRecoveryCapability[] capabilities =
                await dbContext.AdmissionRecoveryCapabilities
                    .IgnoreTenantFilter(
                        TenantFilterBypassReasons.TicketingRecoveryTenantOperation)
                    .Where(value =>
                        value.TenantId == tenantId &&
                        value.ConsumedAt == null &&
                        value.RotatedAt == null)
                    .ToArrayAsync(cancellationToken);
            foreach (AdmissionRecoveryCapability capability in capabilities)
            {
                capability.TryRotate(occurredAtUtc);
            }

            AdmissionTicketCredential[] credentials =
                await dbContext.AdmissionTicketCredentials
                    .IgnoreTenantFilter(
                        TenantFilterBypassReasons.TicketingRecoveryTenantOperation)
                    .Where(value =>
                        value.TenantId == tenantId &&
                        value.AdmissionTicketCredentialStatusId ==
                        (int)AdmissionTicketCredentialStatusEnum.Active)
                    .ToArrayAsync(cancellationToken);
            Guid[] existingReissues =
                await dbContext.TicketingRecoveryReissueIntents
                    .IgnoreTenantFilter(
                        TenantFilterBypassReasons.TicketingRecoveryTenantOperation)
                    .Where(value =>
                        value.TenantId == tenantId &&
                        value.RecoveryOperationId == recoveryOperationId)
                    .Select(value => value.AdmissionTicketId)
                    .ToArrayAsync(cancellationToken);
            foreach (AdmissionTicketCredential credential in credentials)
            {
                credential.Revoke(occurredAtUtc);
                if (!existingReissues.Contains(credential.AdmissionTicketId))
                {
                    dbContext.TicketingRecoveryReissueIntents.Add(
                        TicketingRecoveryReissueIntent.Create(
                            tenantId,
                            recoveryOperationId,
                            credential.AdmissionTicketId,
                            nextCredentialGeneration,
                            occurredAtUtc));
                }
            }

            FairReturnOrchestrationEffect[] effects =
                await dbContext.FairReturnOrchestrationEffects
                    .IgnoreTenantFilter(
                        TenantFilterBypassReasons.TicketingRecoveryTenantOperation)
                    .Where(value =>
                        value.TenantId == tenantId &&
                        value.StatusId !=
                        (int)FairReturnOrchestrationEffectStatus.Completed &&
                        value.StatusId !=
                        (int)FairReturnOrchestrationEffectStatus.DeadLettered)
                    .ToArrayAsync(cancellationToken);
            foreach (FairReturnOrchestrationEffect effect in effects)
            {
                effect.EnterRecovery(nextWorkerFence, occurredAtUtc);
            }

            if (!checkpoint.TryRotateBearerAuthority(
                nextCapabilityGeneration,
                nextCredentialGeneration,
                nextWorkerFence,
                occurredAtUtc))
            {
                throw new InvalidOperationException(
                    "Recovery bearer authority did not advance monotonically.");
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return checkpoint;
        }, cancellationToken);

    public Task<bool> OpenWorkersAsync(
        Guid tenantId,
        Guid recoveryOperationId,
        long expectedWorkerFence,
        DateTime openedAtUtc,
        CancellationToken cancellationToken) =>
        ExecuteSerializableAsync(async () =>
        {
            TicketingRecoveryCheckpoint? checkpoint =
                await QueryCheckpoints().SingleOrDefaultAsync(value =>
                    value.TenantId == tenantId &&
                    value.RecoveryOperationId == recoveryOperationId,
                    cancellationToken);
            if (checkpoint is null ||
                !checkpoint.TryOpenWorkers(expectedWorkerFence, openedAtUtc))
            {
                return false;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);

    public Task<bool> StopSalesAsync(
        Guid tenantId,
        Guid recoveryOperationId,
        long nextWorkerFence,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken) =>
        ExecuteSerializableAsync(async () =>
        {
            TicketingRecoveryCheckpoint? checkpoint =
                await QueryCheckpoints().SingleOrDefaultAsync(value =>
                    value.TenantId == tenantId &&
                    value.RecoveryOperationId == recoveryOperationId,
                    cancellationToken);
            if (checkpoint is null ||
                !checkpoint.StopSales(nextWorkerFence, occurredAtUtc))
            {
                return false;
            }

            FairReturnOrchestrationEffect[] effects =
                await QueryEffects(tenantId)
                    .Where(value =>
                        value.StatusId !=
                        (int)FairReturnOrchestrationEffectStatus.Completed &&
                        value.StatusId !=
                        (int)FairReturnOrchestrationEffectStatus.DeadLettered)
                    .ToArrayAsync(cancellationToken);
            foreach (FairReturnOrchestrationEffect effect in effects)
            {
                effect.EnterRecovery(nextWorkerFence, occurredAtUtc);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);

    public Task<bool> PauseWorkersAsync(
        Guid tenantId,
        Guid recoveryOperationId,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken) =>
        ExecuteSerializableAsync(async () =>
        {
            TicketingRecoveryCheckpoint? checkpoint =
                await QueryCheckpoints().SingleOrDefaultAsync(value =>
                    value.TenantId == tenantId &&
                    value.RecoveryOperationId == recoveryOperationId,
                    cancellationToken);
            if (checkpoint is null ||
                !checkpoint.PauseWorkers(occurredAtUtc))
            {
                return false;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);

    public Task<bool> OpenSalesAsync(
        Guid tenantId,
        Guid recoveryOperationId,
        DateTime openedAtUtc,
        CancellationToken cancellationToken) =>
        ExecuteSerializableAsync(async () =>
        {
            TicketingRecoveryCheckpoint? checkpoint =
                await QueryCheckpoints().SingleOrDefaultAsync(value =>
                    value.TenantId == tenantId &&
                    value.RecoveryOperationId == recoveryOperationId,
                    cancellationToken);
            bool hasAmbiguity = await dbContext.FairReturnOrchestrationEffects
                .IgnoreTenantFilter(
                    TenantFilterBypassReasons.TicketingRecoveryTenantOperation)
                .AnyAsync(value =>
                    value.TenantId == tenantId &&
                    value.StatusId ==
                    (int)FairReturnOrchestrationEffectStatus.Unknown,
                    cancellationToken);
            bool hasPendingReissue =
                await dbContext.TicketingRecoveryReissueIntents
                    .IgnoreTenantFilter(
                        TenantFilterBypassReasons.TicketingRecoveryTenantOperation)
                    .AnyAsync(value =>
                        value.TenantId == tenantId &&
                        value.RecoveryOperationId == recoveryOperationId &&
                        value.Status == TicketingRecoveryReissueStatus.Pending,
                        cancellationToken);
            if (checkpoint is null ||
                hasAmbiguity ||
                hasPendingReissue ||
                !checkpoint.TryOpenSales(openedAtUtc))
            {
                return false;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);

    public async Task<TicketingRecoveryHealth?> GetHealthAsync(
        Guid tenantId,
        Guid recoveryOperationId,
        CancellationToken cancellationToken)
    {
        TicketingRecoveryCheckpoint? checkpoint =
            await GetAsync(tenantId, recoveryOperationId, cancellationToken);
        if (checkpoint is null)
        {
            return null;
        }

        int pendingReissues =
            await dbContext.TicketingRecoveryReissueIntents
                .IgnoreTenantFilter(
                    TenantFilterBypassReasons.TicketingRecoveryTenantOperation)
                .CountAsync(value =>
                    value.TenantId == tenantId &&
                    value.RecoveryOperationId == recoveryOperationId &&
                    value.Status == TicketingRecoveryReissueStatus.Pending,
                    cancellationToken);
        int ambiguousEffects =
            await dbContext.FairReturnOrchestrationEffects
                .IgnoreTenantFilter(
                    TenantFilterBypassReasons.TicketingRecoveryTenantOperation)
                .CountAsync(value =>
                    value.TenantId == tenantId &&
                    value.StatusId ==
                    (int)FairReturnOrchestrationEffectStatus.Unknown,
                    cancellationToken);
        DateTime? oldestPending =
            await dbContext.TicketingRecoveryReissueIntents
                .IgnoreTenantFilter(
                    TenantFilterBypassReasons.TicketingRecoveryTenantOperation)
                .Where(value =>
                    value.TenantId == tenantId &&
                    value.RecoveryOperationId == recoveryOperationId &&
                    value.Status == TicketingRecoveryReissueStatus.Pending)
                .MinAsync(
                    value => (DateTime?)value.CreatedAt,
                    cancellationToken);
        return new TicketingRecoveryHealth(
            checkpoint.Status,
            pendingReissues,
            ambiguousEffects,
            oldestPending);
    }

    public Task<bool> ResolveUnknownAsync(
        Guid tenantId,
        Guid effectId,
        long expectedFence,
        bool retry,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken) =>
        ResolveEffectAsync(
            tenantId,
            effectId,
            expectedFence,
            retry,
            occurredAtUtc,
            cancellationToken);

    public Task<bool> DeadLetterAsync(
        Guid tenantId,
        Guid effectId,
        long expectedFence,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken) =>
        ResolveEffectAsync(
            tenantId,
            effectId,
            expectedFence,
            retry: false,
            occurredAtUtc,
            cancellationToken);

    public async Task<TicketingRecoveryAggregateHealth>
        GetAggregateHealthAsync(
            CancellationToken cancellationToken)
    {
        IQueryable<TicketingRecoveryCheckpoint> checkpoints =
            QueryCheckpoints().AsNoTracking();
        int recoveryOnly = await checkpoints.CountAsync(value =>
            value.Status == TicketingRecoveryStatus.RecoveryOnly,
            cancellationToken);
        int failed = await checkpoints.CountAsync(value =>
            value.Status == TicketingRecoveryStatus.Failed,
            cancellationToken);
        int pendingReissues =
            await dbContext.TicketingRecoveryReissueIntents
                .IgnoreTenantFilter(
                    TenantFilterBypassReasons.TicketingRecoveryTenantOperation)
                .CountAsync(value =>
                    value.Status == TicketingRecoveryReissueStatus.Pending,
                    cancellationToken);
        IQueryable<FairReturnOrchestrationEffect> effects =
            dbContext.FairReturnOrchestrationEffects
                .IgnoreTenantFilter(
                    TenantFilterBypassReasons.TicketingRecoveryTenantOperation)
                .AsNoTracking();
        int ambiguous = await effects.CountAsync(value =>
            value.StatusId ==
            (int)FairReturnOrchestrationEffectStatus.Unknown,
            cancellationToken);
        int deadLettered = await effects.CountAsync(value =>
            value.StatusId ==
            (int)FairReturnOrchestrationEffectStatus.DeadLettered,
            cancellationToken);
        int poison = await effects.CountAsync(value =>
            value.LastFailureCode != null &&
            value.LastFailureCode.StartsWith("POISON_"),
            cancellationToken);
        DateTime? oldestDue = await effects
            .Where(value =>
                value.StatusId ==
                    (int)FairReturnOrchestrationEffectStatus.Pending ||
                value.StatusId ==
                    (int)FairReturnOrchestrationEffectStatus.Unknown)
            .MinAsync(
                value => (DateTime?)value.NextAttemptAt,
                cancellationToken);
        return new TicketingRecoveryAggregateHealth(
            recoveryOnly,
            failed,
            pendingReissues,
            ambiguous,
            deadLettered,
            poison,
            oldestDue);
    }

    private IQueryable<TicketingRecoveryCheckpoint> QueryCheckpoints() =>
        dbContext.TicketingRecoveryCheckpoints
            .IgnoreTenantFilter(
                TenantFilterBypassReasons.TicketingRecoveryTenantOperation);

    private IQueryable<FairReturnOrchestrationEffect> QueryEffects(
        Guid tenantId) =>
        dbContext.FairReturnOrchestrationEffects
            .IgnoreTenantFilter(
                TenantFilterBypassReasons.TicketingRecoveryTenantOperation)
            .Where(value => value.TenantId == tenantId);

    private Task<bool> ResolveEffectAsync(
        Guid tenantId,
        Guid effectId,
        long expectedFence,
        bool retry,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken) =>
        ExecuteSerializableAsync(async () =>
        {
            FairReturnOrchestrationEffect? effect =
                await QueryEffects(tenantId).SingleOrDefaultAsync(value =>
                    value.Id == effectId,
                    cancellationToken);
            if (effect is null ||
                !effect.ResolveRecoveryUnknown(
                    expectedFence,
                    retry,
                    occurredAtUtc))
            {
                return false;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);

    private async Task<T> ExecuteSerializableAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            return await operation();
        }

        return await new EfCoreUnitOfWork(dbContext)
            .ExecuteSerializableAsync(
                _ => operation(),
                cancellationToken);
    }
}
