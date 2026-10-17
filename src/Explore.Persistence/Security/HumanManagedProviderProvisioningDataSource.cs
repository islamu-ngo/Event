using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ManagedProviderProvisioning.Requests.Commands;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Model = Explore.Persistence.Security.ManagedProviderProvisioningDataModel;

namespace Explore.Persistence.Security;

internal sealed class HumanManagedProviderProvisioningDataSource : IHumanManagedProviderProvisioningDataSource
{
    private readonly ExploreDbContext context;
    private readonly ExploreDbContextExecutionLease execution;
    private readonly TimeProvider clock;
    private readonly RelationalConnectionIdentity connection;
    private readonly HumanProvisioningDataQueries queries;
    private readonly DbContextId contextId;
    private readonly HumanCommitSubject subject;
    private EnsureManagedProviderClientProvisionedCommand? original;
    private string? originalCommand;
    private Model.Request? request;
    private bool observed;
    private object? sqliteHandle;

    internal HumanManagedProviderProvisioningDataSource(ExploreDbContext context,
        ExploreDbContextExecutionLease execution, TimeProvider clock, PostgresNativeQueueEnrollment? enrollment)
    {
        HumanSourceProviderOperations.RequirePostgresOrSqlite(context, "human_provisioning_data_provider_not_supported");
        this.context = context;
        this.execution = execution;
        this.clock = clock;
        connection = RelationalConnectionIdentity.Capture(context);
        queries = new(context, connection, enrollment);
        contextId = context.ContextId;
        subject = execution.HumanScope?.Subject
            ?? throw new UnauthorizedAccessException("Provisioning DATA requires its original human owner.");
        RequireSelection();
        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Provisioning DATA must be selected before its owning transaction.");
        Model.RequireInventory(context);
    }

    public async Task<IHumanManagedProviderProvisioningDataObservation> ObserveAsync(
        EnsureManagedProviderClientProvisionedCommand originalRequest, CancellationToken cancellationToken = default)
    {
        RequireSelection();
        if (observed || context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Provisioning DATA observation requires its one idle original command.");
        observed = true;
        original = originalRequest;
        originalCommand = JsonSerializer.Serialize(originalRequest, Model.Json);
        request = await Model.CaptureAsync(originalRequest, subject.PrincipalId, clock, cancellationToken);
        RequireSelection();
        if (connection.State != ConnectionState.Open) await context.Database.OpenConnectionAsync(cancellationToken);
        sqliteHandle = queries.CaptureSqliteHandle();
        var before = await ReadAsync(cancellationToken);
        RequireSelection();
        return new Observation(this, before);
    }

    private void RequireSelection()
    {
        execution.RequireOriginalOwner(context);
        if (context.ContextId != contextId || !connection.Matches(context)
            || !queries.MatchesSqliteHandle(sqliteHandle)
            || context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
            || execution.SelectedTenantId is not null || execution.PolicyPurpose != PolicyMutationPurpose.Standard
            || execution.HumanScope?.Purpose != HumanAdministratorCommitPurpose.GlobalInstance
            || !ReferenceEquals(subject, execution.HumanScope.Subject)
            || execution.HumanLifetime is not { } lifetime || lifetime.AccountKey != subject.AccountKey
            || subject.AccountKey is null || context.ChangeTracker.HasChanges()
            || original is not null && JsonSerializer.Serialize(original, Model.Json) != originalCommand)
            throw new UnauthorizedAccessException("Original provisioning DATA custody changed.");
    }

    private DbTransaction RequireOwner()
    {
        RequireSelection();
        if (!context.HumanCommitAttemptOwnsTransaction || connection.State != ConnectionState.Open
            || System.Transactions.Transaction.Current is not null
            || context.Database.CurrentTransaction?.GetDbTransaction() is not { IsolationLevel: IsolationLevel.Serializable } transaction
            || !connection.Owns(transaction))
            throw new InvalidOperationException("Provisioning DATA requires its original non-replaying Serializable owner.");
        return transaction;
    }

    private Task<JsonObject> ReadAsync(CancellationToken token) => queries.ReadAsync(request!, token);

    private sealed class Observation(HumanManagedProviderProvisioningDataSource source, JsonObject before)
        : IHumanManagedProviderProvisioningDataObservation
    {
        private bool acquired;
        public async Task<IHumanManagedProviderProvisioningData> AcquireAsync(
            EnsureManagedProviderClientProvisionedCommand originalRequest, IPolicySourceSetLease originalSourceSet,
            CancellationToken cancellationToken = default)
        {
            var transaction = source.RequireOwner();
            if (acquired || !ReferenceEquals(source.original, originalRequest))
                throw new UnauthorizedAccessException("Provisioning DATA cannot replay or replace its original command.");
            acquired = true;
            await originalSourceSet.ValidateAsync(cancellationToken);
            var admitted = await source.queries.AcquireAsync(source.request!, before, cancellationToken);
            if (!ReferenceEquals(transaction, source.RequireOwner()))
                throw new UnauthorizedAccessException("Provisioning DATA acquisition changed its original transaction.");
            var data = new Data(source, transaction, admitted);
            // The actual owner samples this after ALL awaited admission callbacks.
            // Omitted Complete or a write after Complete cannot become a success.
            source.context.RegisterHumanCommitAdmission(_ => Task.CompletedTask, data.RequireCompleted);
            return data;
        }
    }

    private sealed class Data : IHumanManagedProviderProvisioningData
    {
        private readonly HumanManagedProviderProvisioningDataSource source;
        private readonly DbTransaction transaction;
        private readonly JsonObject admitted;
        private readonly ManagedProviderProvisioningCompiler.Plan? portablePlan;
        private JsonObject? applied;
        private bool attempted;
        private bool completed;
        private int sqliteChanges;

        internal Data(HumanManagedProviderProvisioningDataSource source, DbTransaction transaction, JsonObject admitted)
        {
            this.source = source;
            this.transaction = transaction;
            this.admitted = admitted;
            try { portablePlan = ManagedProviderProvisioningCompiler.Compile(source.context, source.request!, admitted["Data"]!.AsObject()); }
            catch (ManagedProviderProvisioningCompiler.BusinessRefusal refusal) { Refusal = refusal.Refusal; }
        }

        public HumanManagedProviderProvisioningRefusal? Refusal { get; }
        public bool IsReplay => portablePlan?.Rows.AlreadyProvisioned == true;
        public ProviderAccountKey AdministratorAccount =>
            new((AuthenticationProviderKind)source.request!.Provider, source.request.Account);
        public Guid AdministratorUserId => (portablePlan
            ?? throw new InvalidOperationException("A refused provisioning attempt has no admitted administrator.")).Rows.UserId;

        public Task<HumanManagedProviderProvisioningCapacity> ReadActivationCapacityAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require();
            if (attempted || completed || Refusal is not null)
                throw new InvalidOperationException("Capacity belongs to the admitted pre-effect provisioning attempt.");
            var capacity = admitted["Data"]!["Capacity"]!;
            return Task.FromResult(new HumanManagedProviderProvisioningCapacity(
                (DeploymentMode)capacity["DeploymentMode"]!.GetValue<int>(),
                capacity["ActiveTenants"]!.GetValue<int>(), capacity["ReservedTenants"]!.GetValue<int>()));
        }

        private void Require()
        {
            if (!ReferenceEquals(transaction, source.RequireOwner()))
                throw new UnauthorizedAccessException("Provisioning DATA must retain its original physical transaction.");
        }

        public async Task<HumanManagedProviderProvisioningRows> ApplyAsync(CancellationToken cancellationToken = default)
        {
            Require();
            if (attempted || completed || portablePlan is null) throw new InvalidOperationException("Provisioning DATA apply cannot replay or override refusal.");
            attempted = true;
            applied = await source.queries.ApplyAsync(source.request!, admitted, portablePlan, transaction, cancellationToken);
            Require();
            if (!JsonNode.DeepEquals(applied["Data"], portablePlan.Postimage))
                throw new DbUpdateConcurrencyException("provisioning_data_success_image_mismatch");
            source.queries.RequireRemainder(admitted, applied);
            return portablePlan.Rows;
        }

        public async Task ValidateAsync(CancellationToken cancellationToken = default)
        {
            Require();
            if (completed || applied is null) throw new InvalidOperationException("Provisioning DATA completion requires one applied group.");
            sqliteChanges = await source.queries.CompleteAsync(source.request!, applied, cancellationToken);
            Require();
            completed = true;
        }

        internal void RequireCompleted()
        {
            Require();
            if (!completed || !source.queries.TerminalChangesMatch(sqliteChanges))
                throw new InvalidOperationException("provisioning_data_terminal_seal_missing_or_changed");
        }
    }
}
