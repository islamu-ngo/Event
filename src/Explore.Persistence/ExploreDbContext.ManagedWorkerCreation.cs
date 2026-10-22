using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    private ManagedWorkerCreationContextLease? _managedWorkerCreation;

    internal void PrepareManagedWorkerCreationGraph(ManagedWorkerCreationContextLease custody,
        Guid tenantId, EntityEntry[] effects)
    {
        if (!ReferenceEquals(_managedWorkerCreation, custody))
            throw new UnauthorizedAccessException("managed_worker_creator_selection_invalid");
        custody.PrepareGraph(tenantId, effects);
    }

    internal void PrepareManagedWorkerCreationFailure(ManagedWorkerCreationContextLease custody, EntityEntry operation)
    {
        if (!ReferenceEquals(_managedWorkerCreation, custody))
            throw new UnauthorizedAccessException("managed_worker_creator_selection_invalid");
        custody.PrepareFailure(operation);
    }

    internal EntityEntry[] OriginalManagedWorkerCreationPending(EntityEntry[] pending) =>
        _managedWorkerCreation is { } custody ? custody.RequirePendingGraph(pending) : pending;

    internal Task<int> SavePreparedManagedWorkerCreationChangesAsync(CancellationToken token)
    {
        if (_managedWorkerCreation is not { } custody || Database.CurrentTransaction is null)
            throw new UnauthorizedAccessException("managed_worker_creator_selection_invalid");
        custody.RequireCurrent();
        return SaveWithStorageReferencesAsync(
            acceptChanges => base.SaveChangesAsync(acceptChanges, token),
            acceptAllChangesOnSuccess: true, token);
    }

    private void ResetManagedWorkerCreationContextLease()
    {
        _managedWorkerCreation?.Dispose();
        _managedWorkerCreation = null;
    }

    /// <summary>Execution custody only; no human subject or action authority is created.</summary>
    internal bool HasOriginalCommitContextLease =>
        HasHumanCommitContextLease || HasManagedWorkerCreationContextLease;

    private bool HasManagedWorkerCreationContextLease
    {
        get
        {
            if (_managedWorkerCreation is not { } lease)
                return false;
            lease.RequireCurrent();
            return true;
        }
    }

    internal ManagedWorkerCreationContextLease BindManagedWorkerCreation(
        ManagedTenantWorkerCreationAdmission admission, IManagedTenantWorkerCreationInvocation invocation)
    {
        admission.RequireIssued(invocation);
        RequireBoundTenantLease();
        if (_managedWorkerCreation is not null || HasHumanCommitContextLease || IsTenantFilterBypassed
            || TenantFilterTenantId is not null || Database.CurrentTransaction is not null
            || ChangeTracker.HasChanges())
            throw new UnauthorizedAccessException("managed_worker_creator_selection_invalid");
        return _managedWorkerCreation = new(this, admission, invocation);
    }

    internal sealed class ManagedWorkerCreationContextLease : IDisposable
    {
        private readonly ExploreDbContext context;
        private readonly ManagedTenantWorkerCreationAdmission admission;
        private readonly IManagedTenantWorkerCreationInvocation invocation;
        private readonly DbContextId contextId;
        private readonly RelationalConnectionIdentity connection;
        private readonly int? backend;
        private bool active = true;
        private bool interrupted;
        private PreparedGraph? graph;
        private PreparedEffect? failure;
        private IDbContextTransaction? failureTransaction;
        private bool failureCaptured;

        internal ManagedWorkerCreationContextLease(ExploreDbContext context,
            ManagedTenantWorkerCreationAdmission admission, IManagedTenantWorkerCreationInvocation invocation)
        {
            this.context = context;
            this.admission = admission;
            this.invocation = invocation;
            contextId = context.ContextId;
            connection = RelationalConnectionIdentity.Capture(context);
            if (connection.State != System.Data.ConnectionState.Open)
                throw new UnauthorizedAccessException("managed_worker_creator_selection_invalid");
            backend = connection.BackendProcessId;
            connection.StateChange += Changed;
        }

        internal void RequireCurrent()
        {
            admission.RequireIssued(invocation);
            context.RequireBoundTenantLease();
            if (!active || interrupted || !ReferenceEquals(context._managedWorkerCreation, this)
                || context.ContextId != contextId || context.TenantFilterTenantId is not null
                || context.IsTenantFilterBypassed || context.HasHumanCommitContextLease
                || !connection.Matches(context) || connection.State != System.Data.ConnectionState.Open
                || connection.BackendProcessId != backend)
                throw new UnauthorizedAccessException("managed_worker_creator_selection_invalid");
        }

        internal Task RequireOriginalDeliveryEnvelopeAsync(CancellationToken token)
        {
            RequireCurrent();
            return admission.RequireEnvelopeCurrentAsync(invocation, token);
        }

        internal void PrepareGraph(Guid tenantId, EntityEntry[] effects)
        {
            RequireCurrent();
            EventDiscoveryEnrollmentGate.RequireCurrent(context);
            var transaction = context.Database.CurrentTransaction;
            if (graph is not null || failure is not null || transaction is null
                || transaction.GetDbTransaction().IsolationLevel != System.Data.IsolationLevel.Serializable
                || tenantId == Guid.Empty || tenantId.Version != 7
                || effects.Length == 0 || effects.Any(entry => !ReferenceEquals(entry.Context, context)
                    || entry.State is not (EntityState.Added or EntityState.Modified))
                || effects.Count(entry => entry.Entity is Tenant && entry.State == EntityState.Added) != 1
                || !effects.Any(entry => entry.Entity is Tenant tenant && tenant.Id == tenantId
                    && entry.State == EntityState.Added)
                || effects.Any(entry => entry.Entity is EventDiscoveryRevision))
                throw GraphChanged();
            graph = new(tenantId, transaction, effects.Select(entry => new PreparedEffect(entry)).ToArray());
        }

        internal void PrepareFailure(EntityEntry entry)
        {
            RequireCurrent();
            EventDiscoveryEnrollmentGate.RequireCurrent(context);
            var transaction = context.Database.CurrentTransaction;
            if (graph is not null || failure is not null || transaction is null
                || transaction.GetDbTransaction().IsolationLevel != System.Data.IsolationLevel.Serializable
                || !ReferenceEquals(entry.Context, context) || entry.State != EntityState.Modified
                || entry.Entity is not ManagedTenantProvisioningOperation operation
                || operation.Id != invocation.OperationId || operation.CurrentOutboxMessageId != invocation.OutboxMessageId
                || operation.Status != ManagedTenantProvisioningStatus.Failed || operation.RequestJson is not null
                || operation.TenantId is not null || operation.TenantAdministratorUserId is not null
                || string.IsNullOrWhiteSpace(operation.FailureCode) || operation.FailedAt is null)
                throw GraphChanged();
            failure = new(entry);
            failureTransaction = transaction;
        }

        internal EntityEntry[] RequirePendingGraph(EntityEntry[] pending)
        {
            RequireCurrent();
            EventDiscoveryEnrollmentGate.RequireCurrent(context);
            if (failure is { } refusal)
            {
                if (failureCaptured || !ReferenceEquals(context.Database.CurrentTransaction, failureTransaction)
                    || failureTransaction is null || !connection.Owns(failureTransaction.GetDbTransaction()))
                    throw GraphChanged();
                failureCaptured = true;
                refusal.RequireCurrent();
                if (pending.Length != 1 || !ReferenceEquals(pending[0].Entity, refusal.Entry.Entity)) throw GraphChanged();
                return pending;
            }
            if (graph is not { } prepared || prepared.Captured
                || !ReferenceEquals(context.Database.CurrentTransaction, prepared.Transaction)
                || !connection.Owns(prepared.Transaction.GetDbTransaction()))
                throw GraphChanged();
            try
            {
                foreach (var effect in prepared.Effects)
                    effect.RequireCurrent();
                if (pending.Length != prepared.Effects.Length + 1
                    || pending.Any(entry => !prepared.Effects.Any(effect => ReferenceEquals(effect.Entry.Entity, entry.Entity))
                        && (!context._discoveryEnrollmentCreations.TryGetValue(prepared.TenantId, out var creation)
                            || !ReferenceEquals(creation.Revision, entry.Entity) || creation.Failed || creation.Completed
                            || entry.State != EntityState.Added || creation.Revision.IdentityEpoch != 0
                            || creation.Revision.DisclosureEpoch != 1
                            || !ReferenceEquals(creation.Transaction, prepared.Transaction.GetDbTransaction()))))
                    throw GraphChanged();
                prepared.Captured = true;
                // The original C# graph owns these exact new effects. Tenant and
                // canonical metadata still use the central enrollment lifecycle.
                return pending.Where(entry => !prepared.Effects.Any(effect => effect.Entry.State == EntityState.Added
                    && effect.Entry.Entity is not Tenant && ReferenceEquals(effect.Entry.Entity, entry.Entity))).ToArray();
            }
            catch
            {
                prepared.Captured = true;
                throw;
            }
        }

        private static UnauthorizedAccessException GraphChanged() => new("managed_worker_creator_graph_changed");

        private sealed class PreparedGraph(Guid tenantId, IDbContextTransaction transaction, PreparedEffect[] effects)
        {
            internal Guid TenantId { get; } = tenantId;
            internal IDbContextTransaction Transaction { get; } = transaction;
            internal PreparedEffect[] Effects { get; } = effects;
            internal bool Captured { get; set; }
        }

        private sealed class PreparedEffect
        {
            internal EntityEntry Entry { get; }
            private readonly EntityState state;
            private readonly IProperty[] properties;
            private readonly object?[] values;
            private readonly bool[] modified;

            internal PreparedEffect(EntityEntry entry)
            {
                Entry = entry;
                state = entry.State;
                properties = entry.Metadata.GetProperties().ToArray();
                values = properties.Select(property => Comparer(property)
                    .Snapshot(entry.Property(property.Name).CurrentValue)).ToArray();
                modified = properties.Select(property => entry.Property(property.Name).IsModified).ToArray();
            }

            internal void RequireCurrent()
            {
                if (Entry.State != state || !Entry.Metadata.GetProperties().SequenceEqual(properties)
                    || properties.Where((property, index) => Entry.Property(property.Name).IsModified != modified[index]
                        || !Comparer(property).Equals(Entry.Property(property.Name).CurrentValue, values[index])).Any())
                    throw GraphChanged();
            }

            private static Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer Comparer(IProperty property) =>
                property.GetValueComparer() ?? property.GetTypeMapping().Comparer;
        }

        private void Changed(object? sender, System.Data.StateChangeEventArgs args)
        {
            if (active && args.CurrentState != System.Data.ConnectionState.Open)
                interrupted = true;
        }

        public void Dispose()
        {
            if (!active)
                return;
            active = false;
            connection.StateChange -= Changed;
            // An expired issued slot is not absence and can never mint a fresh owner.
        }
    }
}
