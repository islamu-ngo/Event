using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.DTOs.ManagedProviderProvisioning;
using Explore.Application.Exceptions;
using Explore.Application.Features.Management;
using Explore.Application.Features.Tenants;
using Explore.Application.Management;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Explore.Persistence.Security;

/// <summary>One original admitted generation owns one exact C#/EF graph and physical settlement.</summary>
internal sealed class ManagedTenantWorkerCreationGraphWriter(
    ExploreDbContext context, ExploreDbContextExecutionLease selection,
    IManagedTenantLocalEligibilitySource localEligibility, PrivacyIdentityFenceOperation privacy,
    IPrivacyIdentityFenceAuthority privacyAuthority, IOriginalOwnerSettlementBoundary transactionOwner,
    IEventDiscoveryEnrollmentGate enrollment, ISettingMutationLock mutationLock,
    IOptions<ManagedControlPlaneOptions> options, TimeProvider clock,
    PostgresNativeQueueEnrollment? postgresEnrollment, IdentityDatabaseTopology identityTopology)
{
    private bool attempted;
    private bool settled;

    internal async Task<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>> CreateAsync(
        IManagedTenantWorkerCreationInvocation invocation,
        ExploreDbContext.ManagedWorkerCreationContextLease custody, CancellationToken cancellationToken)
    {
        if (attempted) throw Denied();
        attempted = true;
        try { return await CreateOriginalAsync(invocation, custody, cancellationToken); }
        catch (Exception exception) when (settled && exception is not HumanMutationOutcomeUnknownException)
        {
            throw new HumanMutationOutcomeUnknownException(exception);
        }
    }

    private async Task<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>> CreateOriginalAsync(
        IManagedTenantWorkerCreationInvocation invocation,
        ExploreDbContext.ManagedWorkerCreationContextLease custody, CancellationToken cancellationToken)
    {
        custody.RequireCurrent();
        await invocation.RequireCurrentAsync(cancellationToken);
        bool enabled = options.Value.Enabled;
        int maximum = options.Value.MaximumTenantCount;
        await PostgresManagedTenantWorkerCreationPermissions.RequireCurrentAsync(context, postgresEnrollment,
            identityTopology, cancellationToken);
        var observedOperation = await OperationAsync(invocation.OperationId, cancellationToken);
        var observed = ManagedTenantWorkerCreationSnapshot.CapturePending(observedOperation, invocation.OutboxMessageId);
        var request = observed.ReadRequest();
        var account = ManagedTenantWorkerGraphPlan.Account(observed);
        // Identity lifecycle custody precedes ALL application gates and BEGIN.
        await using var local = request.Administrator.LocalIdentity is { } subject
            ? await localEligibility.AcquireAsync(subject.LocalSubjectId, cancellationToken) : null;
        if (local is not null) RequireEligibility(local.Eligibility, request.Administrator.LocalIdentity!.LocalSubjectId);
        await using var gate = await enrollment.AcquireAsync(cancellationToken);
        var sourcesFence = new PolicySourceSetFence(context, PolicySourceSetPurpose.Global, postgresEnrollment);
        IOriginalOwnerSettlementBoundary owner = privacyAuthority as IOriginalOwnerSettlementBoundary
            ?? transactionOwner;
        return await mutationLock.ExecuteOrderedGroupsAsync(
            [[GovernanceSettingKeys.Deployment.Mode],
                [TenantMutationLockKeys.ForSlug(request.TenantSlug), ManagedTenantProvisioningPreflight.DomainNamespaceMutationKey],
                ManagedTenantProvisioningPolicyInventory.Keys],
            async token =>
            {
                var result = await privacy.ExecuteSettledEnrollmentAsync(account, invocation, owner, async originalToken =>
                {
                    void RequireOwner()
                    {
                        custody.RequireCurrent();
                        selection.RequireOriginalOwner(context);
                        EventDiscoveryEnrollmentGate.RequireCurrent(context);
                        if (!context.HumanCommitAttemptOwnsTransaction || options.Value.Enabled != enabled
                            || options.Value.MaximumTenantCount != maximum
                            || context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                            throw Denied();
                    }
                    RequireOwner();
                    await invocation.RequireCurrentAsync(originalToken);
                    var fence = await sourcesFence.AcquireAsync(originalToken);
                    if (local is not null) await local.RequireCurrentAsync(originalToken);
                    var locked = await LockOperationAsync(observedOperation, observed, originalToken);
                    locked.Start(clock.GetUtcNow().UtcDateTime);
                    var snapshot = ManagedTenantWorkerCreationSnapshot.Capture(locked, invocation.OutboxMessageId);
                    var source = await ManagedTenantWorkerCreationSources.ReadAsync(context, snapshot,
                        local?.Eligibility, originalToken);
                    async Task<OriginalOwnerDecision<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>>> RefuseAsync(
                        string code, string message)
                    {
                        await WriteFailureAsync(custody, source, snapshot, locked, code, local, fence,
                            enabled, maximum, originalToken);
                        RequireOwner();
                        return OriginalOwnerDecision<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>>.Commit(
                            BaseCommandResponse.Failure<ManagedProviderClientProvisioningResultDto>(code, message));
                    }
                    if (!enabled) return await RefuseAsync("managed_mode_disabled", "Managed mode is disabled.");
                    DeploymentMode mode = source.Bootstrap?.Status == InstanceBootstrapStatus.Completed
                        ? source.Bootstrap.Mode : DeploymentMode.SingleTenant;
                    if (mode != DeploymentMode.MultiTenant)
                        return await RefuseAsync("tenant_provisioning_requires_multi_tenant", "Managed tenant provisioning requires MultiTenant mode.");
                    var registration = source.Registrations.FirstOrDefault();
                    var registrationFailure = ManagedTenantProvisioningRegistrationPolicy.EvaluateFacts(
                        registration is null ? null : new(registration.Status, registration.ManagedInstanceId,
                            registration.ApiVersion, registration.Mode), snapshot.ManagedInstanceId, mode);
                    if (registrationFailure is not null) return await RefuseAsync(registrationFailure.Code, registrationFailure.Message);
                    if (source.CustomerBindings.Length != 0)
                        return await RefuseAsync("tenant_provisioning_customer_conflict", "This customer already has a tenant binding.");
                    var preflight = await ManagedTenantProvisioningPreflight.EvaluateFromSourcesAsync(request,
                        requireProvisionablePlan: true, source, originalToken);
                    if (!preflight.Success) return await RefuseAsync(preflight.FailureCode!, preflight.Error!);
                    var capacity = TenantActivationCapacityPolicy.Assess(true, true, mode, maximum,
                        source.Tenants.Count(row => source.Statuses.Any(status => status.Id == row.StatusId && status.Active)),
                        source.Reservations.Count(row => row.Id != snapshot.OperationId
                            && row.Status is ManagedTenantProvisioningStatus.Pending or ManagedTenantProvisioningStatus.Processing));
                    if (!capacity.Allowed) return await RefuseAsync(capacity.FailureCode!, capacity.Error!);
                    if (source.Roles.Length != 1 || source.Roles[0].ScopeId != (int)RoleScopeEnum.Tenant)
                        return await RefuseAsync("tenant_administrator_role_unavailable", "The tenant administrator role is unavailable.");
                    if (!source.Statuses.Any(row => row.Id == (int)TenantStatusEnum.Active && row.Active)) throw Denied();
                    var administrator = source.Administrator;
                    var graph = ManagedTenantWorkerGraphPlan.Build(snapshot, preflight.Resolved!,
                        new(administrator?.User.Id, administrator?.Actor?.Id, administrator?.Login.Id, administrator?.User.EmailVerified),
                        new Role
                        {
                            Id = source.Roles[0].Id,
                            MasterCode = source.Roles[0].Code,
                            FullName = source.Roles[0].Code,
                            RoleScopeId = source.Roles[0].ScopeId
                        }, clock.GetUtcNow().UtcDateTime);
                    if (account.ProviderKind != AuthenticationProviderKind.Local)
                        await privacy.EnsureSubjectMayEnrollAsync(graph.Result.UserId, originalToken);
                    await WriteAsync(invocation, custody, source, snapshot, locked, graph, local, fence,
                        enabled, maximum, originalToken);
                    RequireOwner();
                    return OriginalOwnerDecision<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>>.Commit(
                        BaseCommandResponse.Success(graph.Result));
                }, token);
                settled = true;
                return result;
            }, cancellationToken);
    }

    private async Task WriteAsync(IManagedTenantWorkerCreationInvocation invocation,
        ExploreDbContext.ManagedWorkerCreationContextLease custody, ManagedTenantWorkerCreationSources source,
        ManagedTenantWorkerCreationSnapshot snapshot, ManagedTenantProvisioningOperation operation,
        ManagedTenantWorkerGraphPlan graph, IManagedTenantLocalEligibilityLease? local, IPolicySourceSetLease fence,
        bool enabled, int maximum, CancellationToken token)
    {
        if (context.ChangeTracker.Entries().Any()) throw Denied();
        var transaction = context.Database.CurrentTransaction!;
        var physical = transaction.GetDbTransaction();
        var connection = RelationalConnectionIdentity.Capture(context);
        var contextId = context.ContextId;
        bool completed = false;
        bool failed = false;
        void RequireOwner()
        {
            custody.RequireCurrent();
            if (failed || context.ContextId != contextId || !connection.Matches(context)
                || !ReferenceEquals(context.Database.CurrentTransaction, transaction) || !connection.Owns(physical)
                || !ReferenceEquals(transaction.GetDbTransaction(), physical)) throw Denied();
        }
        ManagedTenantWorkerCreationSources.AdministratorImage? completedAdministrator = source.Administrator;
        if (graph.UpgradeExistingEmailVerified)
        {
            var prior = source.Administrator!.User;
            Guid stamp = Guid.CreateVersion7();
            DateTime now = clock.GetUtcNow().UtcDateTime;
            int changed = await context.Set<User>().Where(row => row.Id == prior.Id && row.ConcurrencyStamp == prior.Stamp
                    && row.EmailVerified == prior.EmailVerified && !row.IsDeleted)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.EmailVerified, (bool?)true)
                    .SetProperty(row => row.ConcurrencyStamp, stamp).SetProperty(row => row.UpdatedAt, now)
                    .SetProperty(row => row.UpdatedBy, (Guid?)null), token);
            if (changed != 1) throw Denied();
            completedAdministrator = source.Administrator with
            {
                User = prior with
                { EmailVerified = true, Stamp = stamp, UpdatedAt = now, UpdatedBy = null }
            };
        }
        context.AddRange(graph.Effects);
        context.Attach(operation);
        snapshot.RequireCurrent(operation);
        operation.Complete(graph.Creation.Tenant.Id, graph.Result.UserId, clock.GetUtcNow().UtcDateTime);
        context.PrepareTrackedEntities();
        context.ChangeTracker.DetectChanges();
        EntityEntry[] effects = context.ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified).ToArray();
        var expected = effects.Select(entry => entry.CurrentValues.Clone()).ToArray();
        context.PrepareManagedWorkerCreationGraph(custody, graph.Creation.Tenant.Id, effects);
        var completedCustomerBindings = graph.Effects.OfType<ExternalBinding>()
            .Where(row => row.ScopeTenantId is null).OrderBy(row => row.Id)
            .Select(row => new ManagedTenantWorkerCreationSources.BindingImage(row.Id, row.ProviderKey,
                row.ExternalSystem, row.ExternalType, row.ExternalId, row.InternalType, row.InternalId,
                row.ScopeTenantId, row.ExternalBindingStatusId)).ToArray();
        string? completedOperation = null;
        void RequireEffects()
        {
            RequireOwner();
            var tracked = context.ChangeTracker.Entries().ToArray();
            if (!completed || effects.Where((entry, index) => entry.State != EntityState.Unchanged
                    || entry.Metadata.GetProperties().Any(property => property.ValueGenerated != ValueGenerated.OnAddOrUpdate
                        && !Equals(entry.Property(property.Name).CurrentValue, expected[index][property.Name]))).Any()
                || tracked.Length != effects.Length + 1
                || tracked.Any(entry => !effects.Any(effect => ReferenceEquals(effect.Entity, entry.Entity))
                    && (entry.Entity is not EventDiscoveryRevision revision || revision.TenantId != graph.Creation.Tenant.Id))
                || !context.RequireDiscoveryEnrollmentCreations().SequenceEqual([graph.Creation.Tenant.Id])) throw Denied();
        }
        context.RegisterHumanCommitAdmission(async finalToken =>
        {
            RequireEffects();
            if (options.Value.Enabled != enabled || options.Value.MaximumTenantCount != maximum) throw Denied();
            await custody.RequireOriginalDeliveryEnvelopeAsync(finalToken);
            if (local is not null) await local.RequireCurrentAsync(finalToken);
            await fence.ValidateAsync(finalToken);
            await PostgresManagedTenantWorkerCreationPermissions.RequireCurrentAsync(context, postgresEnrollment,
                identityTopology, finalToken);
            await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(context, postgresEnrollment, true, finalToken);
            if (JsonSerializer.Serialize(await OperationAsync(snapshot.OperationId, finalToken)) != completedOperation) throw Denied();
            await source.RequireCurrentAsync(new(graph.Creation.Tenant.Id, graph.Creation.Tenant.Slug,
                graph.Creation.Tenant.TenantStatusId), completedAdministrator, completedCustomerBindings, finalToken);
            RequireEffects();
        }, () => { RequireEffects(); custody.RequireCurrent(); });
        try
        {
            RequireOwner();
            int written = await context.SavePreparedManagedWorkerCreationChangesAsync(token);
            if (written != effects.Length + 1) throw Denied();
            completedOperation = JsonSerializer.Serialize(operation);
            if (graph.CreatedUser is not null || graph.CreatedActor is not null || graph.CreatedLogin is not null)
            {
                var user = graph.CreatedUser is { } addedUser
                    ? new ManagedTenantWorkerCreationSources.UserImage(addedUser.Id, addedUser.EmailVerified, addedUser.ConcurrencyStamp,
                        addedUser.IsDeleted, addedUser.DeletedAt, addedUser.DeletedBy, addedUser.UpdatedAt, addedUser.UpdatedBy)
                    : completedAdministrator!.User;
                var actor = graph.CreatedActor is { } addedActor
                    ? new ManagedTenantWorkerCreationSources.ActorImage(addedActor.Id, addedActor.UserId, addedActor.ActorTypeId,
                        addedActor.IsDeleted, addedActor.IsSuspended, addedActor.ConcurrencyStamp) : completedAdministrator?.Actor;
                var login = graph.CreatedLogin is { } addedLogin
                    ? new ManagedTenantWorkerCreationSources.LoginImage(addedLogin.Id, addedLogin.UserId,
                        addedLogin.AuthenticationProviderId, addedLogin.ProviderKey) : completedAdministrator!.Login;
                completedAdministrator = new(user, actor, login);
            }
            completed = true;
            RequireEffects();
        }
        catch { failed = true; throw; }
    }

    private async Task WriteFailureAsync(ExploreDbContext.ManagedWorkerCreationContextLease custody,
        ManagedTenantWorkerCreationSources source, ManagedTenantWorkerCreationSnapshot snapshot,
        ManagedTenantProvisioningOperation operation, string code, IManagedTenantLocalEligibilityLease? local,
        IPolicySourceSetLease fence, bool enabled, int maximum, CancellationToken token)
    {
        if (context.ChangeTracker.Entries().Any()) throw Denied();
        var transaction = context.Database.CurrentTransaction!;
        var physical = transaction.GetDbTransaction();
        var connection = RelationalConnectionIdentity.Capture(context);
        var contextId = context.ContextId;
        bool completed = false;
        bool failed = false;
        context.Attach(operation);
        snapshot.RequireCurrent(operation);
        operation.Fail(code, clock.GetUtcNow().UtcDateTime);
        context.PrepareTrackedEntities();
        context.ChangeTracker.DetectChanges();
        var effect = context.Entry(operation);
        var expected = effect.CurrentValues.Clone();
        context.PrepareManagedWorkerCreationFailure(custody, effect);
        string? completedOperation = null;
        void RequireEffects()
        {
            custody.RequireCurrent();
            EventDiscoveryEnrollmentGate.RequireCurrent(context);
            if (failed || !completed || context.ContextId != contextId || !connection.Matches(context)
                || !connection.Owns(physical) || !ReferenceEquals(context.Database.CurrentTransaction, transaction)
                || !ReferenceEquals(transaction.GetDbTransaction(), physical)
                || effect.State != EntityState.Unchanged || context.ChangeTracker.Entries().Count() != 1
                || !ReferenceEquals(context.ChangeTracker.Entries().Single().Entity, operation)
                || effect.Metadata.GetProperties().Any(property => property.ValueGenerated != ValueGenerated.OnAddOrUpdate
                    && !Equals(effect.Property(property.Name).CurrentValue, expected[property.Name]))) throw Denied();
        }
        context.RegisterHumanCommitAdmission(async finalToken =>
        {
            RequireEffects();
            if (options.Value.Enabled != enabled || options.Value.MaximumTenantCount != maximum) throw Denied();
            await custody.RequireOriginalDeliveryEnvelopeAsync(finalToken);
            if (local is not null) await local.RequireCurrentAsync(finalToken);
            await fence.ValidateAsync(finalToken);
            await PostgresManagedTenantWorkerCreationPermissions.RequireCurrentAsync(context, postgresEnrollment,
                identityTopology, finalToken);
            if (JsonSerializer.Serialize(await OperationAsync(snapshot.OperationId, finalToken)) != completedOperation) throw Denied();
            await source.RequireUnchangedAsync(finalToken);
            RequireEffects();
        }, RequireEffects);
        try
        {
            int written = await context.SavePreparedManagedWorkerCreationChangesAsync(token);
            if (written != 1) throw Denied();
            completedOperation = JsonSerializer.Serialize(operation);
            completed = true;
            RequireEffects();
        }
        catch { failed = true; throw; }
    }

    private async Task<ManagedTenantProvisioningOperation> LockOperationAsync(ManagedTenantProvisioningOperation observed,
        ManagedTenantWorkerCreationSnapshot snapshot, CancellationToken token)
    {
        int fenced = await context.Set<ManagedTenantProvisioningOperation>()
            .Where(row => row.Id == snapshot.OperationId && row.CurrentOutboxMessageId == snapshot.OutboxMessageId
                && row.Status == ManagedTenantProvisioningStatus.Pending && row.RowVersion == snapshot.RowVersion)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Status, row => row.Status), token);
        if (fenced != 1) throw Denied();
        var current = await OperationAsync(snapshot.OperationId, token);
        uint currentVersion = current.RowVersion;
        current.RowVersion = observed.RowVersion;
        snapshot.RequireCurrent(current);
        current.RowVersion = currentVersion;
        return current;
    }

    private Task<ManagedTenantProvisioningOperation> OperationAsync(Guid id, CancellationToken token) =>
        context.Set<ManagedTenantProvisioningOperation>().AsNoTracking().SingleAsync(row => row.Id == id, token);

    private static void RequireEligibility(ManagedTenantLocalEligibility image, Guid subject)
    {
        Guid[] pointers = [image.LocalSubjectId, image.CurrentOperationId, image.PersonalActorId, image.ExternalLoginId];
        if (image.LocalSubjectId != subject || pointers.Any(id => id == Guid.Empty || id.Version != 7)
            || image.Generation <= 0 || image.State is not (LocalIdentityEligibilityState.Ready or LocalIdentityEligibilityState.ChangeRequired))
            throw Denied();
    }

    private static UnauthorizedAccessException Denied() => new("managed_worker_creation_authority_changed");
}
