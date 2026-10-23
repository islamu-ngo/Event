using System.Data.Common;
using System.IO;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

public sealed partial class GroupMemberMutationBoundary
{
    public async Task<BaseCommandResponse<Guid>> ExecuteCreationAsync(Guid tenantId, GroupCreationIds ids,
        Func<Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(operation);
        RequireCaller();
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges()
            || new[] { ids.GroupId, ids.ActorId, ids.ParticipationId, ids.MemberId }.Any(id => id == Guid.Empty)
            || new[] { ids.GroupId, ids.ActorId, ids.ParticipationId, ids.MemberId }.Distinct().Count() != 4) DenyCreation();
        CreationAttempt? attempted = null;
        try
        {
            return await EfCoreUnitOfWork.ExecuteBootstrapConflictRetryAsync(() =>
                context.Database.CreateExecutionStrategy().ExecuteAsync<object?, BaseCommandResponse<Guid>>(null,
                async (_, _, token) =>
                {
                    RequireCaller();
                    if (await VerifyAsync(token)) return attempted!.Result;
                    var actor = await admin.ResolveUserIdAsync(token);
                    if (actor is null || actor == Guid.Empty) DenyCreation();
                    var decision = await authorization.AuthorizeAsync(new AuthorizationRequest(
                        AuthorizationCapabilityCatalog.Require(ResourceKinds.Group, AuthorizationActions.Create),
                        ids.GroupId.ToString("D"), Facts: new GroupAuthorizationFacts(tenantId, ids.GroupId),
                        Subject: new AuthorizationSubject(actor, false), Tenant: new AuthorizationTenant(tenantId)), token);
                    if (!decision.IsAllowed)
                    {
                        if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                            throw new AuthorizationProviderUnavailableException(ResourceKinds.Group, AuthorizationActions.Create);
                        DenyCreation();
                    }
                    context.ChangeTracker.Clear();
                    Guid? epochId = null;
                    long expectedRevision = 0;
                    BaseCommandResponse<Guid>? result = null;
                    async Task RecheckAsync(CancellationToken inner)
                    {
                        RequireCaller();
                        if (await admin.ResolveUserIdAsync(inner) != actor
                            || await privacy.IsSubjectFencedAsync(actor!.Value, inner)
                            || !await context.Users.AsNoTracking().AnyAsync(row => row.Id == actor && !row.IsDeleted, inner)
                            || !await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == tenantId && row.TenantStatus.IsActiveState, inner)
                            || !await new ModerationQualificationAuthority(context, admin, machine)
                                .ProviderTenantMatchesAsync(tenantId, inner)) DenyCreation();
                        if (epochId is { } id && !await context.ModerationQualificationEpochs.AsNoTracking()
                            .AnyAsync(row => row.Id == id && row.TenantId == tenantId && row.Revision == expectedRevision, inner)) DenyCreation();
                        if (result is null) return;
                        var graph = await ReadGraphAsync(inner);
                        if (!result.IsSuccess)
                        {
                            if (graph.Any(row => row is not null)) throw UncertainCreation();
                            return;
                        }
                        if (result.Id != ids.GroupId || !ValidGraph(graph, actor.Value)) throw UncertainCreation();
                        if (attempted is not null && !SameGraph(graph, attempted.Graph)) throw UncertainCreation();
                        // Capture database-normalized scalars after the actual disclosure flush, before commit.
                        attempted ??= new(actor.Value, result, graph, epochId, expectedRevision,
                            epochId is null ? 0 : expectedRevision - 1);
                    }
                    return await privacy.ExecuteEnrollmentSerializedAsync(async privacyToken =>
                        await new EfCoreUnitOfWork(context).ExecuteRecognitionEnrollmentAttemptAsync(async inner =>
                        {
                            result = null;
                            // Standalone hierarchy updates take this namespace before their Tenant source anchor.
                            await using var hierarchy = await RelationalNamedLock.AcquireTransactionAsync(
                                context, $"group-hierarchy:{tenantId}", inner);
                            await using var installation = await RelationalNamedLock.AcquireTransactionAsync(
                                context, "moderation.authority-source.installation", inner);
                            await AcquireHumansAsync(tenantId, [actor!.Value], [(int)RoleEnum.GroupAdmin],
                                PermissionCodes.GroupMemberCreate, inner);
                            if (!await context.Roles.AsNoTracking().AnyAsync(row => row.Id == (int)RoleEnum.GroupAdmin
                                && row.RoleScopeId == (int)RoleScopeEnum.Group, inner)) DenyCreation();
                            var epoch = await AcquireInstalledEpochAsync(tenantId, ResourceKinds.Group,
                                AuthorizationActions.Create, null, inner);
                            epochId = epoch?.Id;
                            expectedRevision = epoch?.Revision ?? 0;
                            await RecheckAsync(inner);
                            if ((await ReadGraphAsync(inner)).Any(row => row is not null)) throw UncertainCreation();
                            result = await operation(actor.Value, inner);
                            if (result.IsSuccess && epoch is not null)
                            {
                                epoch.Advance(expectedRevision);
                                expectedRevision = epoch.Revision;
                                await context.SaveChangesAsync(inner);
                            }
                            return result;
                        }, RecheckAsync, privacyToken), RecheckAsync, token);
                }, async (_, _, token) => new ExecutionResult<BaseCommandResponse<Guid>>(
                    await VerifyAsync(token), attempted is null ? default! : attempted.Result), cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (attempted is not null && TransportFailure(exception))
        {
            if (await VerifyAsync(cancellationToken)) return attempted!.Result;
            throw;
        }

        async Task<bool> VerifyAsync(CancellationToken token)
        {
            if (attempted is not { } receipt) return false;
            RequireCaller();
            if (await admin.ResolveUserIdAsync(token) != receipt.Actor) DenyCreation();
            context.ChangeTracker.Clear();
            return await privacy.ExecuteEnrollmentSerializedAsync(async privacyToken =>
                await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async inner =>
                {
                    var actual = await ReadGraphAsync(inner);
                    var revision = receipt.EpochId is { } id ? await context.ModerationQualificationEpochs.AsNoTracking()
                        .Where(row => row.Id == id).Select(row => (long?)row.Revision).SingleOrDefaultAsync(inner) : 0;
                    if (SameGraph(actual, receipt.Graph) && revision == receipt.AfterRevision) return true;
                    if (actual.All(row => row is null) && revision == receipt.BeforeRevision)
                    { attempted = null; return false; }
                    throw UncertainCreation();
                }, privacyToken), _ => Task.CompletedTask, token);
        }

        async Task<PropertyValues?[]> ReadGraphAsync(CancellationToken token)
        {
            var keys = new (Type Type, Guid Id)[] { (typeof(Group), ids.GroupId), (typeof(Actor), ids.ActorId),
                (typeof(ActorPii), ids.ActorId), (typeof(GroupTenant), ids.ParticipationId), (typeof(GroupMember), ids.MemberId) };
            var rows = new PropertyValues?[keys.Length];
            for (var index = 0; index < keys.Length; index++)
            {
                var entity = await context.FindAsync(keys[index].Type, [keys[index].Id], token);
                if (entity is not null) rows[index] = await context.Entry(entity).GetDatabaseValuesAsync(token);
            }
            return rows;
        }
        bool ValidGraph(PropertyValues?[] graph, Guid actor) => graph.All(row => row is not null)
            && graph[0]!.GetValue<bool>(nameof(Group.IsDeleted)) == false
            && graph[1]!.GetValue<Guid?>(nameof(Actor.GroupId)) == ids.GroupId
            && graph[1]!.GetValue<int>(nameof(Actor.ActorTypeId)) == (int)ActorTypeEnum.Group
            && !graph[1]!.GetValue<bool>(nameof(Actor.IsDeleted)) && !graph[1]!.GetValue<bool>(nameof(Actor.IsSuspended))
            && graph[2]!.GetValue<string>(nameof(ActorPii.DisplayName)) == graph[0]!.GetValue<string>(nameof(Group.FullName))
            && graph[3]!.GetValue<Guid>(nameof(GroupTenant.TenantId)) == tenantId
            && graph[3]!.GetValue<Guid>(nameof(GroupTenant.GroupId)) == ids.GroupId
            && graph[3]!.GetValue<int>(nameof(GroupTenant.ApprovalStatusId)) == (int)ApprovalStatusEnum.Pending
            && !graph[3]!.GetValue<bool>(nameof(GroupTenant.IsDeleted)) && !graph[3]!.GetValue<bool>(nameof(GroupTenant.IsSuspended))
            && !graph[3]!.GetValue<bool>(nameof(GroupTenant.IsOrganizerEligible))
            && graph[4]!.GetValue<Guid>(nameof(GroupMember.TenantId)) == tenantId
            && graph[4]!.GetValue<Guid>(nameof(GroupMember.GroupTenantId)) == ids.ParticipationId
            && graph[4]!.GetValue<Guid>(nameof(GroupMember.UserId)) == actor
            && graph[4]!.GetValue<int>(nameof(GroupMember.RoleId)) == (int)RoleEnum.GroupAdmin
            && !graph[4]!.GetValue<bool>(nameof(GroupMember.IsDeleted));
        void RequireCaller()
        {
            if (tenantId == Guid.Empty || !user.IsAuthenticated || machine.Current is not null
                || context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId) DenyCreation();
        }
    }

    private async Task<ModerationQualificationEpoch?> AcquireInstalledEpochAsync(
        Guid tenantId, string resource, string action, Guid? existingGroupId, CancellationToken token)
    {
        void Deny() => throw new AuthorizationException(resource, action);
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        if (host is not null)
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, host.Id, token);
            await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(context, host.MaintenanceServicePrincipalId, token);
            if (!await context.ServicePrincipals.AsNoTracking().AnyAsync(
                row => row.Id == host.MaintenanceServicePrincipalId && !row.IsDeleted, token)) Deny();
        }
        if (existingGroupId is { } groupId)
        {
            // Ordinary Group/Actor saves own these roots before taking Tenant.
            await RelationalEntityRowFence.AcquireGlobalAsync<Group>(context, groupId, token);
            var actors = await context.Actors.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AsNoTracking()
                .Where(row => row.GroupId == groupId).OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
            foreach (var actorId in actors)
                await RelationalEntityRowFence.AcquireGlobalAsync<Actor>(context, actorId, token);
        }
        await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, tenantId, token);
        var tenant = await context.Tenants.AsNoTracking().SingleOrDefaultAsync(row => row.Id == tenantId, token);
        if (tenant is null) Deny();
        await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(context, [tenant!.TenantStatusId], token);
        if (host is null) return null;
        var discovered = await context.ModerationQualificationEpochs.AsNoTracking()
            .SingleOrDefaultAsync(row => row.HostBindingId == host.Id && row.TenantId == tenantId, token);
        if (discovered is null) Deny();
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(context, discovered!.Id, token);
        var epoch = await context.ModerationQualificationEpochs.SingleAsync(row => row.Id == discovered.Id, token);
        if (epoch.Revision < 1) Deny();
        return epoch;
    }

    private static bool SameGraph(PropertyValues?[] left, PropertyValues?[] right) => left.Length == right.Length
        && left.Zip(right).All(pair => pair.First is null ? pair.Second is null : pair.Second is not null
            && pair.First.Properties.All(property => property.GetValueComparer() is { } comparer
                ? comparer.Equals(pair.First[property], pair.Second[property]) : Equals(pair.First[property], pair.Second[property])));
    private static bool TransportFailure(Exception exception) => exception is DbException or TimeoutException or IOException
        || exception.InnerException is { } inner && TransportFailure(inner);
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void DenyCreation() => throw new AuthorizationException(ResourceKinds.Group, AuthorizationActions.Create);
    private static ConcurrencyConflictException UncertainCreation() => new(ConcurrencyConflictException.ConcurrentUpdate,
        "The group creation cannot be reconciled against its exact committed graph.");
    private sealed record CreationAttempt(Guid Actor, BaseCommandResponse<Guid> Result, PropertyValues?[] Graph,
        Guid? EpochId, long AfterRevision, long BeforeRevision)
    { public override string ToString() => nameof(CreationAttempt); }
}
