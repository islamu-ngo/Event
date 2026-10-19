using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Compilation;
using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Explore.Application.Features.ConfigurationManifest.Preflight;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Explore.Persistence.Security;

/// <summary>
/// One nonhuman mutation attempt, with a separate evidence-only action available
/// exclusively after this owner's acknowledged whole rollback.
/// </summary>
internal sealed partial class ConfigurationManifestBootstrapOwnerFactory(
    IGlobalExploreDbContextFactory globalFactory,
    ConfigurationManifestBootstrapOrigin authority,
    IConfiguration configuration, TimeProvider clock,
    IConfigurationManifestEffectDeliveryStrategy effectDelivery)
    : IConfigurationManifestBootstrapOwnerFactory
{
    // Observation-only instrumentation for the actual physical lock/source boundaries.
    // It cannot replace acquisition, validation, native persistence or completion.
    internal Func<string, ExploreDbContext, CancellationToken, Task>? BeforeOriginalLock { get; set; }
    internal Func<ExploreDbContext, ConfigurationManifestApplyPlan, CancellationToken, Task>? AfterOriginalObservation { get; set; }
    public async Task<ConfigurationManifestPreflightResult> ValidateAsync(ConfigurationManifestApplyPlan plan, CancellationToken token)
    {
        if (plan.Mode != ConfigurationManifestMode.ValidateOnly)
            throw new UnauthorizedAccessException("The read-only manifest source cannot originate bootstrap.");
        await using var context = await globalFactory.CreateDbContextAsync(token);
        ConfigurationManifestBootstrapProviderOperations.RequireSupported(context);
        context.ValidateTenantLease();
        if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
            || context.HasHumanCommitContextLease || context.IsConfigurationInstanceImportOwner)
            throw new UnauthorizedAccessException("Manifest validation requires its selected nonhuman Global reader.");
        await context.Database.OpenConnectionAsync(token);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var data = new ConfigurationManifestBootstrapData(context);
        var scope = Reserve(plan).Scope;
        var image = await new ConfigurationManifestBootstrapProviderOperations(context, data, configuration)
            .ReadValidationAsync(scope, token);
        var result = ConfigurationManifestPreflight.EvaluateOriginal(plan, Snapshot(data, image) with { BootstrapState = null });
        if (AfterOriginalObservation is { } observe) await observe(context, plan, token);
        await transaction.RollbackAsync(token);
        return result;
    }

    public async Task<BaseCommandResponse<Guid>> ApplyAsync(IConfigurationManifestBootstrapSource source, CancellationToken token)
    {
        if (!ReferenceEquals(configuration, authority.Configuration))
            throw new UnauthorizedAccessException("Bootstrap cannot substitute its original deployment configuration.");
        var file = authority.Claim(source);
        await using var context = await globalFactory.CreateDbContextAsync(token);
        ConfigurationManifestBootstrapProviderOperations.RequireSupported(context);
        await context.Database.OpenConnectionAsync(token);
        var owner = new Owner(context, source, authority, configuration);
        await owner.RequireAsync(token);
        await owner.AttestAsync(token);
        Guid operationId = Guid.CreateVersion7();
        DateTime occurred = Instant(context, clock.GetUtcNow().UtcDateTime);
        ConfigurationManifestApplyPlan plan;
        try
        {
            var manifest = JsonSerializer.Deserialize(file.Content.AsSpan(),
                ConfigurationPortabilityJsonContext.Default.ConfigurationManifestV1Alpha2)
                ?? throw new JsonException("Manifest root is absent.");
            plan = ConfigurationManifestCompiler.Compile(new(manifest, ConfigurationManifestMode.Bootstrap,
                file.Digest, file.Content.Length), operationId, occurred);
        }
        catch (ConfigurationManifestCompilationException exception)
        {
            return Failure(operationId, exception.FailureCode);
        }
        catch (JsonException)
        {
            return Failure(operationId, ConfigurationManifestIngestionFailureCodes.JsonInvalid);
        }
        await effectDelivery.DrainPendingAsync(token);
        await owner.RequireAsync(token);
        var reserved = Reserve(plan);
        var scope = reserved.Scope;
        var locks = new List<IAsyncDisposable>();
        Rejection? rejected = null;
        ConfigurationManifestOperation? operation = null;
        try
        {
            foreach (string key in RelationalSettingMutationLock.NormalizeOrderedSettingKeyGroups(
                         ConfigurationManifestLockKeys.CompileOrderedGroups(plan).Select(group => group.AsEnumerable())))
            {
                if (BeforeOriginalLock is { } observe) await observe(key, context, token);
                locks.Add(await RelationalNamedLock.AcquireSessionAsync(context, $"explore:setting-mutation:{key}", token));
            }
            await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token))
            {
                owner.Bind(transaction);
                var before = await owner.ReadAsync(scope, token);
                ConfigurationManifestBootstrapData.RetainObservedIdentities(scope, before);
                await owner.RequireImageAsync(scope, before, token);
                if (AfterOriginalObservation is { } observe) await observe(context, plan, token);
                // The first native row/absence image is complete before any business preflight.
                var preflight = ConfigurationManifestPreflight.EvaluateOriginal(plan, Snapshot(owner.Data, before));
                plan = preflight.BoundPlan;
                if (!preflight.IsValid)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    rejected = new(this, source, owner.Target, plan, scope, before, preflight.Errors[0].Code,
                        Instant(context, clock.GetUtcNow().UtcDateTime));
                }
                else
                {
                    var compiled = Compile(owner.Data, reserved, before, preflight,
                        Instant(context, clock.GetUtcNow().UtcDateTime));
                    await owner.RequireImageAsync(scope, before, token);
                    Completion? completed = null;
                    try
                    {
                        completed = await owner.WriteMutationAsync(scope, before, compiled, token);
                    }
                    catch (DbException exception) when (!token.IsCancellationRequested)
                    {
                        owner.RequirePhysical();
                        await transaction.RollbackAsync(CancellationToken.None);
                        rejected = new(this, source, owner.Target, plan, scope, before,
                            exception is PostgresException { SqlState: "40001" }
                                ? ConfigurationManifestApplicationFailureCodes.WriteConflict
                                : ConfigurationManifestApplicationFailureCodes.ApplyFailed,
                            Instant(context, clock.GetUtcNow().UtcDateTime));
                    }
                    if (rejected is null)
                    {
                        await owner.RequireImageAsync(scope, compiled.After, token);
                        RequireCompleted(owner, compiled, completed
                            ?? throw new UnauthorizedAccessException("The original native writer did not complete."));
                        // A lost acknowledgement propagates. It is never classified as rejection.
                        await transaction.CommitAsync(token);
                        operation = compiled.Operation;
                    }
                }
            }
            owner.End();
        }
        finally
        {
            for (int index = locks.Count - 1; index >= 0; index--) await locks[index].DisposeAsync();
        }
        if (rejected is not null)
        {
            // Rejection is private and only created after the real original rollback ACK.
            await CommitEvidenceAsync(rejected, token);
            return Failure(plan.OperationId, rejected.Code);
        }
        if (operation?.EffectOutboxId is { } effect)
            await effectDelivery.DeliverAsync(effect, token);
        return BaseCommandResponse.Success(plan.OperationId, "Configuration manifest bootstrap completed.");
    }

    private async Task CommitEvidenceAsync(Rejection rejection, CancellationToken token)
    {
        if (!ReferenceEquals(rejection.Issuer, this) || Interlocked.Exchange(ref rejection.Consumed, 1) != 0)
            throw new UnauthorizedAccessException("Only the original acknowledged rollback may admit failed bootstrap history.");
        await authority.RequireOriginalAsync(rejection.Source, token);
        await using var context = await globalFactory.CreateDbContextAsync(token);
        await context.Database.OpenConnectionAsync(token);
        var owner = new Owner(context, rejection.Source, authority, configuration);
        if (owner.Target != rejection.Target)
            throw new UnauthorizedAccessException("Bootstrap failure evidence cannot change database or namespace.");
        var locks = new List<IAsyncDisposable>();
        try
        {
            foreach (string key in RelationalSettingMutationLock.NormalizeOrderedSettingKeyGroups(
                         ConfigurationManifestLockKeys.CompileOrderedGroups(rejection.Plan).Select(group => group.AsEnumerable())))
                locks.Add(await RelationalNamedLock.AcquireSessionAsync(context, $"explore:setting-mutation:{key}", token));
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            owner.Bind(transaction);
            // These are fresh EVIDENCE preimages, not a new configuration baseline.
            // Only the original rejected operation row may be inserted on this owner.
            var evidenceBefore = await owner.ReadAsync(rejection.Scope, token);
            await owner.RequireAsync(token);
            var failed = Operation(rejection.Plan, ConfigurationManifestOperationStatus.Failed, 0, 0,
                rejection.Plan.Tenants.Length, rejection.Code, [], [], rejection.ObservedAt);
            var after = evidenceBefore.DeepClone().AsObject();
            var changes = new JsonArray();
            Add(owner.Data, after, changes, failed);
            owner.Data.Order(after);
            if (changes.Count != 1 || changes[0]!["Tag"]!.GetValue<string>() != "Operations")
                throw new UnauthorizedAccessException("Failed bootstrap evidence is not configuration mutation authority.");
            await owner.WriteEvidenceAsync(rejection, evidenceBefore, changes, after, token);
            await owner.RequireImageAsync(rejection.Scope, after, token);
            await transaction.CommitAsync(token);
        }
        finally
        {
            for (int index = locks.Count - 1; index >= 0; index--) await locks[index].DisposeAsync();
        }
    }

    private static Reservations Reserve(ConfigurationManifestApplyPlan plan)
    {
        var directories = plan.Tenants.ToImmutableDictionary(tenant => tenant.PlannedTenantId, _ => Guid.CreateVersion7());
        var policies = plan.Tenants.Where(tenant => tenant.PaidEventPolicy is not null)
            .ToImmutableDictionary(tenant => tenant.PlannedTenantId, _ => Guid.CreateVersion7());
        Guid? instancePolicy = plan.Instance.PaidEventPolicy?.ProposedRevision is null ? null : Guid.CreateVersion7();
        return new(new JsonObject
        {
            ["Slugs"] = JsonSerializer.SerializeToNode(plan.Tenants.Select(tenant => tenant.Slug)),
            ["Tenants"] = JsonSerializer.SerializeToNode(plan.Tenants.Select(tenant => tenant.PlannedTenantId)),
            ["CreatedTenants"] = JsonSerializer.SerializeToNode(plan.Tenants.Select(tenant => tenant.PlannedTenantId)),
            ["Documents"] = JsonSerializer.SerializeToNode(plan.Tenants.Select(tenant => tenant.BrandingDocument.DocumentId).Concat(directories.Values)),
            ["Policies"] = JsonSerializer.SerializeToNode(policies.Values.Concat(instancePolicy is { } id ? [id] : [])),
            ["Operations"] = JsonSerializer.SerializeToNode(new[] { plan.OperationId }),
            ["Operation"] = JsonValue.Create(plan.OperationId),
            ["Effect"] = JsonValue.Create(plan.EffectOutboxId)
        }, directories, policies, instancePolicy);
    }
    private sealed record Reservations(JsonObject Scope, ImmutableDictionary<Guid, Guid> Directories,
        ImmutableDictionary<Guid, Guid> Policies, Guid? InstancePolicy);

    private static DateTime Instant(ExploreDbContext context, DateTime value) =>
        ConfigurationManifestBootstrapProviderOperations.Instant(context, value);
    private static BaseCommandResponse<Guid> Failure(Guid id, string code) =>
        BaseCommandResponse.Failure<Guid>(code, "The manifest attempt did not apply configuration.", [code], id);
    private sealed class Rejection(ConfigurationManifestBootstrapOwnerFactory issuer, IConfigurationManifestBootstrapSource source,
        string target, ConfigurationManifestApplyPlan plan, JsonObject scope, JsonObject before, string code, DateTime observedAt)
    {
        internal ConfigurationManifestBootstrapOwnerFactory Issuer => issuer;
        internal IConfigurationManifestBootstrapSource Source => source;
        internal string Target => target;
        internal ConfigurationManifestApplyPlan Plan => plan;
        internal JsonObject Scope => scope;
        internal JsonObject Before => before;
        internal string Code => code;
        internal DateTime ObservedAt => observedAt;
        internal int Consumed;
    }
    private sealed record Compiled(ConfigurationManifestOperation Operation, JsonArray Changes, JsonObject After);
    private sealed record Completion(Owner Owner, Compiled Compiled, DbTransaction Transaction);
    private static void RequireCompleted(Owner owner, Compiled compiled, Completion completion)
    {
        owner.RequirePhysical();
        if (!ReferenceEquals(completion.Owner, owner) || !ReferenceEquals(completion.Compiled, compiled)
            || !ReferenceEquals(completion.Transaction, owner.Transaction))
            throw new UnauthorizedAccessException("The actual bootstrap writer must complete its original physical unit.");
    }

    private sealed class Owner
    {
        private readonly ExploreDbContext context;
        private readonly IConfigurationManifestBootstrapSource source;
        private readonly IConfigurationManifestBootstrapSourceAuthority authority;
        private readonly RelationalConnectionIdentity connection;
        private readonly DbContextId contextId;
        private readonly int? backend;
        private readonly ConfigurationManifestBootstrapProviderOperations provider;
        private int writeConsumed;
        internal ConfigurationManifestBootstrapData Data { get; }
        internal string Target { get; }
        internal DbTransaction? Transaction { get; private set; }
        internal Owner(ExploreDbContext context, IConfigurationManifestBootstrapSource source,
            IConfigurationManifestBootstrapSourceAuthority authority, IConfiguration configuration)
        {
            this.context = context; this.source = source; this.authority = authority;
            connection = RelationalConnectionIdentity.Capture(context); contextId = context.ContextId;
            backend = connection.BackendProcessId;
            Data = new(context);
            Target = connection.DataSource + "\n" + connection.Database + "\n" + string.Join("\n", Data.Relations.Select(relation => relation.Table));
            provider = new(context, Data, configuration);
            RequirePhysical();
        }
        internal void Bind(IDbContextTransaction transaction)
        {
            if (Transaction is not null) throw new UnauthorizedAccessException("Bootstrap cannot replace its physical transaction.");
            Transaction = transaction.GetDbTransaction();
            RequirePhysical();
        }
        internal void End()
        {
            if (context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("Original bootstrap settlement has not finished.");
            Transaction = null;
            RequirePhysical();
        }
        internal void RequirePhysical()
        {
            context.ValidateTenantLease();
            if (context.ContextId != contextId || !connection.Matches(context)
                || connection.State != ConnectionState.Open || context.TenantFilterTenantId is not null
                || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner || context.HasHumanCommitContextLease
                || context.HumanCommitAuditSubject is not null || context.CurrentUserService?.IsAuthenticated == true
                || connection.BackendProcessId is { } processId && processId != backend
                || Target != connection.DataSource + "\n" + connection.Database + "\n" + string.Join("\n", Data.Relations.Select(relation => relation.Table))
                || Transaction is not null && (!ReferenceEquals(Transaction, context.Database.CurrentTransaction?.GetDbTransaction())
                    || !connection.Owns(Transaction) || Transaction.IsolationLevel != IsolationLevel.Serializable))
                throw new UnauthorizedAccessException("Original nonhuman Global context, connector and namespace custody changed.");
        }
        internal async Task RequireAsync(CancellationToken token)
        {
            RequirePhysical();
            await authority.RequireOriginalAsync(source, token);
            RequirePhysical();
        }
        internal Task AttestAsync(CancellationToken token) => provider.AttestAsync(token);
        internal async Task<JsonObject> ReadAsync(JsonObject scope, CancellationToken token)
        {
            RequirePhysical();
            var image = await provider.ReadAsync(scope, token);
            Data.Order(image);
            RequirePhysical();
            return image;
        }
        internal async Task RequireImageAsync(JsonObject scope, JsonObject expected, CancellationToken token)
        {
            if (!ConfigurationManifestBootstrapData.Same(expected, await ReadAsync(scope, token)))
                throw new UnauthorizedAccessException("The original bootstrap rows, metadata, absence or effects changed.");
            await RequireAsync(token);
        }
        internal async Task<Completion> WriteMutationAsync(JsonObject scope, JsonObject before, Compiled compiled, CancellationToken token)
        {
            if (compiled.Operation.Status != ConfigurationManifestOperationStatus.Applied
                || Interlocked.Exchange(ref writeConsumed, 1) != 0)
                throw new UnauthorizedAccessException("The original bootstrap writer cannot replay or change its outcome.");
            await RequireImageAsync(scope, before, token);
            await provider.WriteMutationAsync(scope, before, compiled.Changes, compiled.After, token);
            await RequireImageAsync(scope, compiled.After, token);
            return new(this, compiled, Transaction!);
        }
        internal async Task WriteEvidenceAsync(Rejection rejection, JsonObject before, JsonArray changes,
            JsonObject after, CancellationToken token)
        {
            if (!ReferenceEquals(rejection.Source, source) || rejection.Target != Target || rejection.Consumed != 1
                || Interlocked.Exchange(ref writeConsumed, 1) != 0 || changes.Count != 1
                || changes[0]!["Tag"]!.GetValue<string>() != "Operations"
                || changes[0]!["Before"] is not null
                || changes[0]!["After"]!["Id"]!.GetValue<Guid>() != rejection.Plan.OperationId
                || !JsonNode.DeepEquals(changes[0]!["After"]!["Status"],
                    Data.ProviderValue("Operations", nameof(ConfigurationManifestOperation.Status), ConfigurationManifestOperationStatus.Failed)))
                throw new UnauthorizedAccessException("Only original acknowledged rollback evidence may write failed history.");
            await RequireImageAsync(rejection.Scope, before, token);
            await provider.WriteEvidenceAsync(rejection.Scope, before, changes, after, token);
            await RequireImageAsync(rejection.Scope, after, token);
        }
    }
}
