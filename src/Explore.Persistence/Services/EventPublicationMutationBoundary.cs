using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

/// <summary>Joins current Human publication restrictions to the actual Event and outbox commit.</summary>
public sealed class EventPublicationMutationBoundary(
    ExploreDbContext context, IPrivacyIdentityFenceAuthority privacy, IUserContext user,
    IAdminContext identity, IMachinePrincipalAccessor machine, TimeProvider clock,
    IModerationRecognitionContinuityReader continuity) : IEventPublicationMutationBoundary
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(Guid eventId,
        Func<CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Publication requires its own native attempt without pending caller changes.");
        Guid tenantId = context.TenantFilterTenantId ?? Guid.Empty;
        if (eventId == Guid.Empty || tenantId == Guid.Empty || !user.IsAuthenticated
            || machine.Current is not null || context.IsTenantFilterBypassed)
            throw Changed();
        Guid? caller = await identity.ResolveUserIdAsync(cancellationToken);
        if (caller is not { } human || human == Guid.Empty || user.GetRequiredUserId() != human) throw Changed();

        try
        {
            return await EfCoreUnitOfWork.ExecuteBootstrapConflictRetryAsync(() =>
                context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                context.ChangeTracker.Clear();
                EventPublicationModerationFacts? original = null;
                BaseCommandResponse<Guid>? result = null;
                EventAuthorityProviderOperations.AuthorityPlan? plan = null;
                Host? host = null;
                DateTimeOffset? acquiredAt = null;
                async Task RecheckAsync(CancellationToken token)
                {
                    if (context.TenantFilterTenantId != tenantId || context.IsTenantFilterBypassed
                        || !user.IsAuthenticated || machine.Current is not null || user.GetRequiredUserId() != human
                        || await identity.ResolveUserIdAsync(token) != human
                        || await privacy.IsSubjectFencedAsync(human, token)
                        || !await context.Users.AsNoTracking().AnyAsync(row => row.Id == human && !row.IsDeleted, token)
                        || !await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == tenantId && row.TenantStatus.IsActiveState, token)
                        || !await new ModerationQualificationAuthority(context, identity, machine).ProviderTenantMatchesAsync(tenantId, token)
                        || await ReadHostAsync(token) != host
                        || host is not null && !await context.ServicePrincipals.AsNoTracking()
                            .AnyAsync(row => row.Id == host.MaintenanceServicePrincipalId && !row.IsDeleted, token))
                        throw Changed();
                    var now = clock.GetUtcNow();
                    if (acquiredAt is { } start && now < start) throw Changed();
                    if (plan is not null)
                        await new EventAuthoritySnapshotService(context).GetCommitBoundForUsersAndEventsAsync(
                            tenantId, [human], [eventId], now.UtcDateTime, token, plan);
                    if (original is null || result?.IsSuccess != true) return;
                    var current = await new EventPublicationModerationSnapshotReader(context, continuity).ReadAsync(human, now, token);
                    if (current != original || !original.PermitsAt(clock.GetUtcNow())) throw Changed();
                }

                // The outer privacy callback is also required: co-located identity storage owns the real commit.
                return await privacy.ExecuteEnrollmentSerializedAsync(async token =>
                    await new EfCoreUnitOfWork(context).ExecuteRecognitionEnrollmentAsync(async inner =>
                {
                    await using var installation = await RelationalNamedLock.AcquireTransactionAsync(context,
                        "moderation.authority-source.installation", inner);
                    plan = await EventAuthorityProviderOperations.ReadPlanAsync(context, tenantId, [human], [eventId], inner);
                    await EventAuthoritySnapshotService.AcquireHumanSourcesAsync(context, tenantId, [human],
                        plan.Assignments.Select(row => row.RoleId).ToArray(), null, inner);
                    host = await ReadHostAsync(inner);
                    if (host is not null)
                    {
                        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, host.Id, inner);
                        await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(context, host.MaintenanceServicePrincipalId, inner);
                    }
                    foreach (var actor in plan.Parents.Select(row => row.ActorId)
                        .Concat(plan.Parents.Where(row => row.OrganizerActorId.HasValue).Select(row => row.OrganizerActorId!.Value))
                        .Distinct().OrderBy(id => id.ToString("N"), StringComparer.Ordinal))
                        await RelationalEntityRowFence.AcquireGlobalAsync<Actor>(context, actor, inner);
                    await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, tenantId, inner);
                    var tenant = await context.Tenants.AsNoTracking().SingleOrDefaultAsync(row => row.Id == tenantId, inner);
                    if (tenant is null) throw Changed();
                    await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(context, [tenant.TenantStatusId], inner);
                    if (host is not null)
                    {
                        var epoch = await context.ModerationQualificationEpochs.AsNoTracking()
                            .SingleOrDefaultAsync(row => row.HostBindingId == host.Id && row.TenantId == tenantId, inner);
                        if (epoch is null || epoch.Revision <= 0) throw Changed();
                        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(context, epoch.Id, inner);
                    }
                    acquiredAt = clock.GetUtcNow();
                    await RecheckAsync(inner);
                    // These are the writer's actual jurisdiction slots, including absence/first adoption.
                    await using var instanceSlot = host is null ? null : await RelationalNamedLock.AcquireTransactionAsync(context,
                        $"moderation.publication:{host.Id:N}:{host.InstanceId:N}", inner);
                    await using var tenantSlot = host is null ? null : await RelationalNamedLock.AcquireTransactionAsync(context,
                        $"moderation.publication:{host.Id:N}:{tenantId:N}", inner);
                    original = await new EventPublicationModerationSnapshotReader(context, continuity).ReadAsync(human, acquiredAt.Value, inner);
                    if (!original.PermitsAt(clock.GetUtcNow()))
                        return BaseCommandResponse.Failure<Guid>(original.Outcome == ModerationRestrictionOutcome.Denied
                            ? "event_publish_restricted" : "event_publish_authority_unavailable",
                            "Current publication authority does not permit this operation.", id: eventId);
                    result = await operation(inner);
                    return result;
                }, RecheckAsync, token), RecheckAsync, cancellationToken);
            }), cancellationToken);
        }
        finally { context.ModerationTrustDomainId = null; }
    }

    private Task<Host?> ReadHostAsync(CancellationToken token) => context.ModerationHostBindings.AsNoTracking()
        .Select(row => new Host(row.Id, row.TrustDomainId, row.SolutionId, row.InstanceId, row.MaintenanceServicePrincipalId))
        .SingleOrDefaultAsync(token);

    private static ConcurrencyConflictException Changed() => new(ConcurrencyConflictException.ConcurrentUpdate,
        "Current publication authority changed before the owning commit.");

    private sealed record Host(Guid Id, Guid TrustDomainId, Guid SolutionId, Guid InstanceId, Guid MaintenanceServicePrincipalId);
}
