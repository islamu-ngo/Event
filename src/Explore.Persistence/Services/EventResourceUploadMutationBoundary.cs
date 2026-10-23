using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Exceptions;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

/// <summary>Serializes upload commits with participating authority writers and privacy recovery.</summary>
public sealed class EventResourceUploadMutationBoundary(
    ExploreDbContext context, IPrivacyIdentityFenceAuthority privacy,
    IEventResourceModerationSnapshotReader moderation, TimeProvider clock) : IEventResourceUploadMutationBoundary
{
    public async Task<BaseCommandResponse<StorageUploadSessionDto>> ExecuteAsync(
        EventResourceAuthorityRequest request, EventResourceModerationFacts original,
        Func<CancellationToken, Task<BaseCommandResponse<StorageUploadSessionDto>>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(operation);
        if (!request.IsUploadMutation || !request.HasConsistentUploadPurpose)
            throw new ArgumentException("An explicit resource upload mutation is required.", nameof(request));

        return await EfCoreUnitOfWork.ExecuteBootstrapConflictRetryAsync(
            () => context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            BaseCommandResponse<StorageUploadSessionDto>? result = null;
            async Task RecheckAsync(CancellationToken token)
            {
                // Failure can retire staging or release quota; an upload restriction must not veto cleanup.
                if (result?.IsSuccess != true) return;
                var now = clock.GetUtcNow();
                var current = await moderation.ReadAsync(request, now, token);
                if (current != original || !current.PermitsNativeEvaluationAt(clock.GetUtcNow()))
                    throw new ConcurrencyConflictException(ConcurrencyConflictException.ConcurrentUpdate,
                        "Resource upload authority changed before commit.");
            }

            return await privacy.ExecuteEnrollmentSerializedAsync(async token =>
                await new EfCoreUnitOfWork(context).ExecuteRecognitionEnrollmentAsync(async inner =>
                {
                    result = null;
                    // The installer uses this same transaction-owned fence before an epoch exists.
                    await using var installation = await RelationalNamedLock.AcquireTransactionAsync(
                        context, "moderation.authority-source.installation", inner);
                    if (request.SubjectUserId is { } userId)
                        await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, userId, inner);
                    var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(inner);
                    // Instance-scoped restrictions can be written from another tenant.
                    if (host is not null)
                        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, host.Id, inner);
                    // Installation writes this existing anchor too: stale PostgreSQL snapshots abort,
                    // including absent-host replays. Discovery writers also require Tenant before epoch.
                    await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, request.TenantId, inner);
                    if (!await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == request.TenantId, inner))
                        throw new ConcurrencyConflictException(ConcurrencyConflictException.ConcurrentUpdate,
                            "Resource upload tenant is unavailable.");
                    if (host is not null)
                    {
                        var epoch = await context.ModerationQualificationEpochs.AsNoTracking().SingleOrDefaultAsync(
                            row => row.HostBindingId == host.Id && row.TenantId == request.TenantId, inner);
                        if (epoch is not null)
                            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(
                                context, epoch.Id, inner);
                    }
                    result = await operation(inner);
                    return result;
                }, RecheckAsync, token), RecheckAsync, cancellationToken);
        }), cancellationToken);
    }
}
