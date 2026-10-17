using System.Data;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Domain.Enums;
using Explore.Domain.Constants;
using Explore.Domain.Secrets;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence;

public sealed class RelationalAuthenticationCredentialCutoverCoordinator(
    IDbContextFactory<ExploreDbContext> contextFactory,
    IAdminContext adminContext,
    IHttpContextAccessor httpContextAccessor,
    IAuthenticationCredentialProfileReader profileReader) : IAuthenticationCredentialCutoverCoordinator
{
    public async Task<AuthenticationCredentialCleanupInventory> InspectCleanupAsync(
        Guid instanceId, Guid operationId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        Guid actorId = await RequireActorAsync(cancellationToken);
        await using var planning = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await planning.Database.CreateExecutionStrategy().ExecuteAsync(() =>
            InspectCleanupTransactionAsync(instanceId, operationId, actorId, now, cancellationToken));
    }

    private async Task<AuthenticationCredentialCleanupInventory> InspectCleanupTransactionAsync(
        Guid instanceId, Guid operationId, Guid actorId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await RequireCurrentOperatorAsync(db, instanceId, actorId, cancellationToken);
        var repository = new AuthenticationCredentialCutoverRepository(db);
        var head = await repository.GetHeadAsync(instanceId, cancellationToken)
            ?? throw new InvalidOperationException("authentication_cutover_not_current");
        if (head.CurrentOperationId != operationId)
            throw new InvalidOperationException("authentication_cutover_not_current");
        var operation = await repository.GetAsync(instanceId, operationId, cancellationToken)
            ?? throw new InvalidOperationException("authentication_cutover_not_current");
        RequireProfile(operation);
        if (now >= operation.Deadline)
            throw new InvalidOperationException("authentication_cutover_expired");
        if (operation.State != AuthenticationCredentialCutover.CutoverState.ReadyForCleanup)
            throw new InvalidOperationException("authentication_cutover_not_ready");
        var inventory = await ReadCleanupInventoryAsync(db, operation, head, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return inventory;
    }

    public async Task<AuthenticationCredentialCutover> CleanupAsync(
        Guid instanceId, Guid operationId, Guid deploymentRevision,
        Guid expectedHeadStamp, Guid expectedOperationStamp,
        IReadOnlyCollection<Guid> approvedSettingIds, IReadOnlyCollection<Guid> approvedBindingIds,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        Guid actorId = await RequireActorAsync(cancellationToken);
        ArgumentNullException.ThrowIfNull(approvedSettingIds);
        ArgumentNullException.ThrowIfNull(approvedBindingIds);
        var settings = approvedSettingIds.Take(3).ToArray();
        var bindings = approvedBindingIds.Take(3).ToArray();
        if (settings.Length > 2 || bindings.Length > 2
            || settings.Any(id => id == Guid.Empty) || bindings.Any(id => id == Guid.Empty)
            || settings.Distinct().Count() != settings.Length || bindings.Distinct().Count() != bindings.Length)
            throw new ArgumentException("authentication_cutover_invalid_cleanup_targets");
        await using var planning = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await planning.Database.CreateExecutionStrategy().ExecuteAsync(() =>
            CleanupTransactionAsync(instanceId, operationId, deploymentRevision, expectedHeadStamp,
                expectedOperationStamp, settings, bindings, actorId, now, cancellationToken));
    }

    private async Task<AuthenticationCredentialCutover> CleanupTransactionAsync(
        Guid instanceId, Guid operationId, Guid deploymentRevision, Guid expectedHeadStamp,
        Guid expectedOperationStamp, Guid[] settings, Guid[] bindings, Guid actorId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await RequireCurrentOperatorAsync(db, instanceId, actorId, cancellationToken);
            var repository = new AuthenticationCredentialCutoverRepository(db);
            var retained = await repository.GetAsync(instanceId, operationId, cancellationToken)
                ?? throw new InvalidOperationException("authentication_cutover_not_current");
            // A committed cleanup is permanent. Recovery reauthorizes, but never reapplies
            // supplied targets or reopens a receipt after a later replacement/profile change.
            if (retained.State == AuthenticationCredentialCutover.CutoverState.Cleaned)
            {
                if (retained.DeploymentRevision != deploymentRevision)
                    throw new InvalidOperationException("authentication_cutover_wrong_target");
                await transaction.CommitAsync(cancellationToken);
                return retained;
            }
            var head = await repository.GetHeadAsync(instanceId, cancellationToken)
                ?? throw new InvalidOperationException("authentication_cutover_not_current");
            head.Guard(operationId, expectedHeadStamp);
            await repository.SaveHeadAsync(head, expectedHeadStamp, cancellationToken);
            var operation = await repository.GetAsync(instanceId, operationId, cancellationToken)
                ?? throw new InvalidOperationException("authentication_cutover_not_current");
            if (operation.DeploymentRevision != deploymentRevision)
                throw new InvalidOperationException("authentication_cutover_wrong_target");
            RequireProfile(operation);
            if (operation.ConcurrencyStamp != expectedOperationStamp)
                throw new InvalidOperationException("authentication_cutover_stale_stamp");
            try
            {
                operation.EnsureCleanupAllowed(operationId, expectedOperationStamp, now);
            }
            catch (InvalidOperationException) when (
                operation.State == AuthenticationCredentialCutover.CutoverState.Expired
                && operation.ConcurrencyStamp != expectedOperationStamp)
            {
                await repository.SaveAsync(operation, expectedOperationStamp, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw;
            }
            var inventory = await ReadCleanupInventoryAsync(db, operation, head, cancellationToken);
            if (!settings.ToHashSet().SetEquals(inventory.Settings.Select(row => row.Id))
                || !bindings.ToHashSet().SetEquals(inventory.Bindings.Select(row => row.Id)))
                throw new InvalidOperationException("authentication_cutover_cleanup_targets_changed");
            int settingCount = await db.SystemSettings.Where(row => settings.Contains(row.Id))
                .ExecuteDeleteAsync(cancellationToken);
            int bindingCount = await db.SecretBindings.Where(row => bindings.Contains(row.Id))
                .ExecuteDeleteAsync(cancellationToken);
            if (settingCount != settings.Length || bindingCount != bindings.Length)
                throw new InvalidOperationException("authentication_cutover_cleanup_targets_changed");
            operation.CompleteCleanup(operationId, expectedOperationStamp, settingCount, bindingCount, now);
            await repository.SaveAsync(operation, expectedOperationStamp, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return operation;
        }
        catch
        {
            if (db.Database.CurrentTransaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<AuthenticationCredentialCleanupInventory> ReadCleanupInventoryAsync(
        ExploreDbContext db, AuthenticationCredentialCutover operation, AuthenticationCredentialCutoverHead head,
        CancellationToken cancellationToken)
    {
        var settings = await db.SystemSettings.AsNoTracking().Where(row =>
                row.SettingKey == InfrastructureSecretSettingKeys.Authentication.GoogleClientSecret
                || row.SettingKey == InfrastructureSecretSettingKeys.Authentication.KeycloakClientSecret)
            .Select(row => new AuthenticationCredentialCleanupInventory.Target(row.Id, row.SettingKey))
            .ToListAsync(cancellationToken);
        var bindings = await db.SecretBindings.AsNoTracking().Where(row =>
                row.SettingScopeId == (int)ConfigurationScopeEnum.Instance && row.ScopeId == null
                && row.Qualifier == string.Empty
                && (row.SettingKey == SecretDefinitionRegistry.Keys.Authentication.GoogleClientSecret
                    || row.SettingKey == SecretDefinitionRegistry.Keys.Keycloak.BlazorClientSecret))
            .Select(row => new SecretBinding
            {
                Id = row.Id,
                SettingKey = row.SettingKey,
                SettingScopeId = row.SettingScopeId,
                ScopeId = row.ScopeId,
                Qualifier = row.Qualifier,
                SecretSourceTypeId = row.SecretSourceTypeId,
                InfisicalEnvironment = row.InfisicalEnvironment,
                InfisicalPath = row.InfisicalPath,
                InfisicalKey = row.InfisicalKey,
                EnvironmentVariableName = row.EnvironmentVariableName
            }).ToListAsync(cancellationToken);
        return new(operation.Id, operation.DeploymentRevision, head.ConcurrencyStamp, operation.ConcurrencyStamp,
            settings.Where(row => row.SettingKey is InfrastructureSecretSettingKeys.Authentication.GoogleClientSecret
                or InfrastructureSecretSettingKeys.Authentication.KeycloakClientSecret),
            bindings.Where(row => row.Qualifier == string.Empty
                && AuthenticationCredentialCustodyPolicy.IsRuntimeCredentialKey(row.SettingKey)
                && !AuthenticationCredentialCustodyPolicy.HasCanonicalRuntimeReference(row))
                .Select(row => new AuthenticationCredentialCleanupInventory.Target(row.Id, row.SettingKey)));
    }

    public async Task<AuthenticationCredentialCutover> PrepareAsync(
        AuthenticationCredentialCutover operation, Guid? expectedCurrentOperationId,
        Guid? expectedHeadStamp, Guid? expectedPredecessorStamp, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        Guid actorId = await RequireActorAsync(cancellationToken);
        await using var planning = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await planning.Database.CreateExecutionStrategy().ExecuteAsync(() =>
            PrepareTransactionAsync(operation, expectedCurrentOperationId, expectedHeadStamp,
                expectedPredecessorStamp, actorId, now, cancellationToken));
    }

    private async Task<AuthenticationCredentialCutover> PrepareTransactionAsync(
        AuthenticationCredentialCutover operation, Guid? expectedCurrentOperationId,
        Guid? expectedHeadStamp, Guid? expectedPredecessorStamp, Guid actorId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.State != AuthenticationCredentialCutover.CutoverState.Prepared)
            throw new InvalidOperationException("authentication_cutover_invalid_preparation");
        if (now < operation.CreatedAt || now >= operation.Deadline)
            throw new InvalidOperationException("authentication_cutover_invalid_preparation_time");
        if (operation.CreatedBy != actorId)
            throw new UnauthorizedAccessException("authentication_cutover_operator_conflict");
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await RequireCurrentOperatorAsync(db, operation.InstanceId, actorId, cancellationToken);
            RequireProfile(operation);
            var repository = new AuthenticationCredentialCutoverRepository(db);
            var head = await repository.GetHeadAsync(operation.InstanceId, cancellationToken);
            if (head is null)
            {
                if (expectedCurrentOperationId is not null || expectedHeadStamp is not null
                    || expectedPredecessorStamp is not null)
                    throw new InvalidOperationException("authentication_cutover_head_conflict");
                await repository.AddAsync(operation, cancellationToken);
                await repository.AddHeadAsync(AuthenticationCredentialCutoverHead.Create(operation), cancellationToken);
            }
            else
            {
                if (expectedCurrentOperationId is not Guid currentId || expectedHeadStamp is not Guid headStamp
                    || expectedPredecessorStamp is not Guid predecessorStamp)
                    throw new InvalidOperationException("authentication_cutover_head_conflict");
                head.Guard(currentId, headStamp);
                await repository.SaveHeadAsync(head, headStamp, cancellationToken);
                var predecessor = await repository.GetAsync(operation.InstanceId, currentId, cancellationToken)
                    ?? throw new InvalidOperationException("authentication_cutover_not_current");
                if (predecessor.ConcurrencyStamp != predecessorStamp)
                    throw new InvalidOperationException("authentication_cutover_stale_stamp");
                if (predecessor.State is AuthenticationCredentialCutover.CutoverState.Prepared
                    or AuthenticationCredentialCutover.CutoverState.Collecting
                    or AuthenticationCredentialCutover.CutoverState.ReadyForCleanup)
                {
                    try
                    {
                        predecessor.Supersede(predecessorStamp, now);
                    }
                    catch (InvalidOperationException) when (
                        predecessor.State == AuthenticationCredentialCutover.CutoverState.Expired)
                    {
                        await repository.SaveAsync(predecessor, predecessorStamp, cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        throw;
                    }
                    await repository.SaveAsync(predecessor, predecessorStamp, cancellationToken);
                }
                await repository.AddAsync(operation, cancellationToken);
                Guid guardedStamp = head.ConcurrencyStamp;
                head.Replace(operation, currentId, guardedStamp);
                await repository.SaveHeadAsync(head, guardedStamp, cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return operation;
        }
        catch
        {
            if (db.Database.CurrentTransaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<AuthenticationCredentialCutover> ReportActivationAsync(
        AuthenticationCredentialCutover.ActivationReport report, Guid expectedHeadStamp,
        Guid expectedOperationStamp, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        Guid actorId = await RequireActorAsync(cancellationToken);
        return await MutateAsync(report.InstanceId, report.OperationId, expectedHeadStamp, expectedOperationStamp,
            actorId, operation =>
            {
                if (operation.State == AuthenticationCredentialCutover.CutoverState.Prepared)
                    operation.StartCollecting(operation.ConcurrencyStamp, now);
                operation.ReportActivation(report, operation.ConcurrencyStamp, now);
            }, cancellationToken);
    }

    public async Task<AuthenticationCredentialCutover> AttestFenceAsync(
        AuthenticationCredentialCutover.FenceAttestation attestation, Guid expectedHeadStamp,
        Guid expectedOperationStamp, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attestation);
        Guid actorId = await RequireActorAsync(cancellationToken);
        if (attestation.AttestedBy != actorId)
            throw new UnauthorizedAccessException("authentication_cutover_operator_conflict");
        return await MutateAsync(attestation.InstanceId, attestation.OperationId, expectedHeadStamp,
            expectedOperationStamp, actorId, operation =>
            {
                if (operation.State == AuthenticationCredentialCutover.CutoverState.Prepared)
                    operation.StartCollecting(operation.ConcurrencyStamp, now);
                operation.AttestFence(attestation, operation.ConcurrencyStamp, now);
            }, cancellationToken);
    }

    private async Task<AuthenticationCredentialCutover> MutateAsync(
        Guid instanceId, Guid operationId, Guid expectedHeadStamp, Guid expectedOperationStamp,
        Guid actorId, Action<AuthenticationCredentialCutover> mutation,
        CancellationToken cancellationToken)
    {
        await using var planning = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await planning.Database.CreateExecutionStrategy().ExecuteAsync(() =>
            MutateTransactionAsync(instanceId, operationId, expectedHeadStamp,
                expectedOperationStamp, actorId, mutation, cancellationToken));
    }

    private async Task<AuthenticationCredentialCutover> MutateTransactionAsync(
        Guid instanceId, Guid operationId, Guid expectedHeadStamp, Guid expectedOperationStamp,
        Guid actorId, Action<AuthenticationCredentialCutover> mutation,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await RequireCurrentOperatorAsync(db, instanceId, actorId, cancellationToken);
            var repository = new AuthenticationCredentialCutoverRepository(db);
            var head = await repository.GetHeadAsync(instanceId, cancellationToken)
                ?? throw new InvalidOperationException("authentication_cutover_not_current");
            head.Guard(operationId, expectedHeadStamp);
            await repository.SaveHeadAsync(head, expectedHeadStamp, cancellationToken);
            var operation = await repository.GetAsync(instanceId, operationId, cancellationToken)
                ?? throw new InvalidOperationException("authentication_cutover_not_current");
            if (operation.ConcurrencyStamp != expectedOperationStamp)
                throw new InvalidOperationException("authentication_cutover_stale_stamp");
            RequireProfile(operation);
            try
            {
                mutation(operation);
            }
            catch (InvalidOperationException) when (
                operation.State == AuthenticationCredentialCutover.CutoverState.Expired
                && operation.ConcurrencyStamp != expectedOperationStamp)
            {
                await repository.SaveAsync(operation, expectedOperationStamp, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw;
            }
            await repository.SaveAsync(operation, expectedOperationStamp, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return operation;
        }
        catch
        {
            if (db.Database.CurrentTransaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<Guid> RequireActorAsync(CancellationToken cancellationToken)
    {
        if (httpContextAccessor.HttpContext?.User.Identities.Any(identity => identity.IsAuthenticated) != true
            || await adminContext.ResolveUserIdAsync(cancellationToken) is not Guid actorId || actorId == Guid.Empty)
            throw new UnauthorizedAccessException("authentication_cutover_operator_required");
        return actorId;
    }

    private async Task RequireCurrentOperatorAsync(
        ExploreDbContext db, Guid instanceId, Guid actorId, CancellationToken cancellationToken)
    {
        var bootstrap = await new InstanceBootstrapStateRepository(db).GetCurrentForUpdate(cancellationToken);
        if (bootstrap is null || bootstrap.Status != InstanceBootstrapStatus.Completed)
            throw new UnauthorizedAccessException("authentication_cutover_instance_unavailable");
        if (bootstrap.Id != instanceId)
            throw new InvalidOperationException("authentication_cutover_instance_conflict");
        var authority = new ExternalApiKeyIssuanceAuthority(db, httpContextAccessor);
        if (!await authority.IsInstanceAdministratorForCommitAsync(actorId, cancellationToken))
            throw new UnauthorizedAccessException("authentication_cutover_operator_required");
    }

    private void RequireProfile(AuthenticationCredentialCutover operation)
    {
        var profile = profileReader.ReadCurrent();
        if (operation.Authority != profile.Authority || operation.AuthorityProfileRevision != profile.Revision)
            throw new InvalidOperationException("authentication_cutover_profile_conflict");
    }
}
