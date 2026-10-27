using System.Data.Common;
using System.IO;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.Organizations.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

public sealed partial class OrganizationMemberMutationBoundary
{
    public async Task<BaseCommandResponse<Guid>> ExecuteCreationAsync(Guid tenantId, OrganizationCreationIds ids,
        Func<Guid, bool, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(operation);
        RequireCreator();
        var proposedIds = new[] { ids.OrganizationId, ids.ActorId, ids.ParticipationId, ids.MemberId };
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges()
            || proposedIds.Any(id => id == Guid.Empty) || proposedIds.Distinct().Count() != proposedIds.Length) DenyCreation();
        CreationAttempt? attempted = null;
        var admission = new NativeHumanMutationAdmission(context, finalizer);
        try
        {
            return await EfCoreUnitOfWork.ExecuteBootstrapConflictRetryAsync(() =>
                context.Database.CreateExecutionStrategy().ExecuteAsync<object?, BaseCommandResponse<Guid>>(null,
                async (_, _, token) =>
                {
                    RequireCreator();
                    if (await VerifyAsync(token)) return attempted!.Result;
                    var creator = await admin.ResolveUserIdAsync(token);
                    if (creator is null || creator == Guid.Empty) DenyCreation();
                    var decision = await authorization.AuthorizeAsync(new AuthorizationRequest(
                        AuthorizationCapabilityCatalog.Require(ResourceKinds.Organization, AuthorizationActions.Create),
                        CreateOrganizationCommand.PreCreateResourceId, Facts: new PreCreateAuthorizationFacts(tenantId),
                        Subject: new AuthorizationSubject(creator, false), Tenant: new AuthorizationTenant(tenantId)), token);
                    if (!decision.IsAllowed)
                    {
                        if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                            throw new AuthorizationProviderUnavailableException(ResourceKinds.Organization, AuthorizationActions.Create);
                        DenyCreation();
                    }
                    context.ChangeTracker.Clear();
                    Guid? epochId = null;
                    long expectedRevision = 0;
                    bool? approved = null;
                    BaseCommandResponse<Guid>? result = null;
                    async Task RecheckAsync(CancellationToken inner)
                    {
                        RequireCreator();
                        if (await admin.ResolveUserIdAsync(inner) != creator
                            || await privacy.IsSubjectFencedAsync(creator!.Value, inner)
                            || !await context.Users.AsNoTracking().AnyAsync(row => row.Id == creator && !row.IsDeleted, inner)
                            || !await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == tenantId && row.TenantStatus.IsActiveState, inner)
                            || !await new ModerationQualificationAuthority(context, admin, machine)
                                .ProviderTenantMatchesAsync(tenantId, inner)) DenyCreation();
                        if (epochId is { } id && !await context.ModerationQualificationEpochs.AsNoTracking()
                            .AnyAsync(row => row.Id == id && row.TenantId == tenantId && row.Revision == expectedRevision, inner)) DenyCreation();
                        if (approved is { } original && await admin.IsTenantAdminAsync(tenantId, inner) != original) DenyCreation();
                        if (result is null) return;
                        var graph = await ReadGraphAsync(inner);
                        if (!result.IsSuccess)
                        {
                            if (graph.Any(row => row is not null)) throw UncertainCreation();
                            return;
                        }
                        if (result.Id != ids.OrganizationId || !ValidGraph(graph, creator.Value, approved!.Value)) throw UncertainCreation();
                        if (attempted is not null && !SameGraph(graph, attempted.Graph)) throw UncertainCreation();
                        // Preserve database-normalized scalars only after the actual disclosure flush.
                        attempted ??= new(creator.Value, result, graph, epochId, expectedRevision,
                            epochId is null ? 0 : expectedRevision - 1);
                    }
                    return await privacy.ExecuteEnrollmentSerializedAsync(async privacyToken =>
                        await new EfCoreUnitOfWork(context).ExecuteRecognitionEnrollmentAttemptAsync(async inner =>
                        {
                            admission.RegisterOwning(RecheckAsync);
                            result = null;
                            await using var installation = await RelationalNamedLock.AcquireTransactionAsync(
                                context, "moderation.authority-source.installation", inner);
                            await AcquireHumansAsync(tenantId, [creator!.Value], (int)RoleEnum.OrgAdmin, inner);
                            if (!await context.Roles.AsNoTracking().AnyAsync(row => row.Id == (int)RoleEnum.OrgAdmin
                                && row.RoleScopeId == (int)RoleScopeEnum.Organization, inner)) DenyCreation();
                            var epoch = await AcquireInstalledEpochAsync(tenantId, ResourceKinds.Organization,
                                AuthorizationActions.Create, null, inner);
                            epochId = epoch?.Id;
                            expectedRevision = epoch?.Revision ?? 0;
                            await RecheckAsync(inner);
                            approved = await admin.IsTenantAdminAsync(tenantId, inner);
                            if ((await ReadGraphAsync(inner)).Any(row => row is not null)) throw UncertainCreation();
                            result = await operation(creator.Value, approved.Value, inner);
                            if (result.IsSuccess && epoch is not null)
                            {
                                epoch.Advance(expectedRevision); expectedRevision = epoch.Revision;
                                await context.SaveChangesAsync(inner);
                            }
                            return result;
                        }, RecheckAsync, privacyToken), RecheckAsync, token);
                }, async (_, _, token) => new ExecutionResult<BaseCommandResponse<Guid>>(
                    await VerifyAsync(token), attempted is null ? default! : attempted.Result), cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (attempted is not null && CreationTransportFailure(exception))
        {
            if (await VerifyAsync(cancellationToken)) return attempted!.Result;
            throw;
        }

        async Task<bool> VerifyAsync(CancellationToken token)
        {
            if (attempted is not { } receipt) return false;
            RequireCreator();
            if (await admin.ResolveUserIdAsync(token) != receipt.Creator) DenyCreation();
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
            var keys = new (Type Type, Guid Id)[] { (typeof(Organization), ids.OrganizationId),
                (typeof(OrganizationPii), ids.OrganizationId), (typeof(Actor), ids.ActorId), (typeof(ActorPii), ids.ActorId),
                (typeof(OrganizationTenant), ids.ParticipationId), (typeof(OrganizationMember), ids.MemberId) };
            var rows = new PropertyValues?[keys.Length];
            for (var index = 0; index < keys.Length; index++)
            {
                var entity = await context.FindAsync(keys[index].Type, [keys[index].Id], token);
                if (entity is not null) rows[index] = await context.Entry(entity).GetDatabaseValuesAsync(token);
            }
            return rows;
        }
        bool ValidGraph(PropertyValues?[] graph, Guid creator, bool approved) => graph.All(row => row is not null)
            && !graph[0]!.GetValue<bool>(nameof(Organization.IsDeleted))
            && graph[2]!.GetValue<Guid?>(nameof(Actor.OrganizationId)) == ids.OrganizationId
            && graph[2]!.GetValue<int>(nameof(Actor.ActorTypeId)) == (int)ActorTypeEnum.Organization
            && !graph[2]!.GetValue<bool>(nameof(Actor.IsDeleted)) && !graph[2]!.GetValue<bool>(nameof(Actor.IsSuspended))
            && graph[3]!.GetValue<string>(nameof(ActorPii.DisplayName)) == graph[1]!.GetValue<string>(nameof(OrganizationPii.FullName))
            && graph[4]!.GetValue<Guid>(nameof(OrganizationTenant.TenantId)) == tenantId
            && graph[4]!.GetValue<Guid>(nameof(OrganizationTenant.OrganizationId)) == ids.OrganizationId
            && graph[4]!.GetValue<int>(nameof(OrganizationTenant.ApprovalStatusId)) == (approved
                ? (int)ApprovalStatusEnum.Approved : (int)ApprovalStatusEnum.Pending)
            && graph[4]!.GetValue<bool>(nameof(OrganizationTenant.IsVisible)) == approved
            && graph[4]!.GetValue<bool>(nameof(OrganizationTenant.IsOrganizerEligible)) == approved
            && graph[4]!.GetValue<Guid?>(nameof(OrganizationTenant.ApprovedBy)) == (approved ? creator : (Guid?)null)
            && graph[4]!.GetValue<DateTime?>(nameof(OrganizationTenant.ApprovedAt)).HasValue == approved
            && !graph[4]!.GetValue<bool>(nameof(OrganizationTenant.IsDeleted)) && !graph[4]!.GetValue<bool>(nameof(OrganizationTenant.IsSuspended))
            && graph[5]!.GetValue<Guid>(nameof(OrganizationMember.TenantId)) == tenantId
            && graph[5]!.GetValue<Guid>(nameof(OrganizationMember.OrganizationTenantId)) == ids.ParticipationId
            && graph[5]!.GetValue<Guid>(nameof(OrganizationMember.UserId)) == creator
            && graph[5]!.GetValue<int>(nameof(OrganizationMember.RoleId)) == (int)RoleEnum.OrgAdmin
            && !graph[5]!.GetValue<bool>(nameof(OrganizationMember.IsDeleted));
        void RequireCreator()
        {
            if (tenantId == Guid.Empty || !user.IsAuthenticated || machine.Current is not null
                || context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId) DenyCreation();
        }
    }

    private async Task<ModerationQualificationEpoch?> AcquireInstalledEpochAsync(
        Guid tenantId, string resource, string action, Guid? existingOrganizationId, CancellationToken token)
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
        if (existingOrganizationId is { } organizationId)
        {
            // Ordinary Organization/Pii sources hold their root and Actors before Tenant.
            await RelationalEntityRowFence.AcquireGlobalAsync<Organization>(context, organizationId, token);
            var actorIds = await context.Actors.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AsNoTracking()
                .Where(row => row.OrganizationId == organizationId).OrderBy(row => row.Id)
                .Select(row => row.Id).ToArrayAsync(token);
            foreach (var actorId in actorIds)
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
    private static bool CreationTransportFailure(Exception exception) => exception is DbException or TimeoutException or IOException
        || exception.InnerException is { } inner && CreationTransportFailure(inner);
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void DenyCreation() => throw new AuthorizationException(ResourceKinds.Organization, AuthorizationActions.Create);
    private static ConcurrencyConflictException UncertainCreation() => new(ConcurrencyConflictException.ConcurrentUpdate,
        "The organization creation cannot be reconciled against its exact committed graph.");
    private sealed record CreationAttempt(Guid Creator, BaseCommandResponse<Guid> Result, PropertyValues?[] Graph,
        Guid? EpochId, long AfterRevision, long BeforeRevision)
    { public override string ToString() => nameof(CreationAttempt); }
}
