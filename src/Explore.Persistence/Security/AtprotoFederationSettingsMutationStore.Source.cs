using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Settings;
using Explore.Domain.Settings;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

public sealed partial class AtprotoFederationSettingsMutationStore
{
    private sealed class Source : IAtprotoFederationSettingsSource, IDisposable
    {
        private readonly ExploreDbContext context;
        private readonly ExploreDbContextExecutionLease selection;
        private readonly DbContextId contextId;
        private readonly RelationalConnectionIdentity connection;
        private bool released;
        private bool opened;
        private int? backend;
        private string? dataSource;
        private string? database;

        internal Source(AtprotoFederationSettingsMutationStore store, ExploreDbContext context,
            ExploreDbContextExecutionLease selection, OrdinarySettingsMutation mutation,
            HumanCommitSubject subject, DateTime occurredAt)
        {
            Store = store;
            this.context = context;
            this.selection = selection;
            Ordinary = mutation;
            Subject = subject;
            OccurredAt = occurredAt;
            TenantId = selection.SelectedTenantId;
            contextId = context.ContextId;
            connection = RelationalConnectionIdentity.Capture(context);
            Effects = mutation.Effects.Where(effect => AtprotoFederationSettingPolicy.IsDedicatedKey(effect.Key)).ToImmutableArray();
            ParentKeys = Effects.Value.Select(effect => effect.Key).ToArray();
            foreach (var effect in Effects.Value) RequireEffectShape(effect);
            connection.StateChange += ConnectionChanged;
        }

        internal Source(AtprotoFederationSettingsMutationStore store, ExploreDbContext context,
            ExploreDbContextExecutionLease selection, MixedSettingsBatchMutation mutation, DateTime occurredAt)
        {
            Store = store;
            this.context = context;
            this.selection = selection;
            Mixed = mutation;
            Subject = mutation.Source.Subject;
            OccurredAt = occurredAt;
            TenantId = selection.SelectedTenantId;
            contextId = context.ContextId;
            connection = RelationalConnectionIdentity.Capture(context);
            ParentKeys = mutation.Original.Values.Keys.Where(AtprotoFederationSettingPolicy.IsDedicatedKey).ToArray();
            // Accepted is deliberately unavailable during idle observation.
            // Only the genuine active mixed compiler may freeze this partition.
            connection.StateChange += ConnectionChanged;
        }

        internal AtprotoFederationSettingsMutationStore Store { get; }
        internal OrdinarySettingsMutation? Ordinary { get; }
        internal MixedSettingsBatchMutation? Mixed { get; }
        internal HumanCommitSubject Subject { get; }
        internal Guid? TenantId { get; }
        internal DateTime OccurredAt { get; set; }
        internal string[] ParentKeys { get; }
        internal ImmutableArray<HumanSettingsMutation>? Effects { get; set; }
        internal DataImage? Expected { get; set; }
        internal DataImage? Original { get; set; }
        internal SettingsDiscoveryDisclosureObservation? Discovery { get; set; }
        internal DbTransaction? Transaction { get; set; }
        internal Dictionary<string, CompletedEffect> Completed { get; } = new(StringComparer.Ordinal);
        internal bool Writing { get; set; }
        internal int State;

        internal void CaptureOpenConnection()
        {
            if (opened || connection.State != ConnectionState.Open)
                throw new UnauthorizedAccessException("Federation observation must retain one original open connector.");
            backend = connection.BackendProcessId;
            dataSource = connection.DataSource;
            database = connection.Database;
            opened = true;
        }

        internal void RequireSelection()
        {
            selection.RequireOriginalOwner(context);
            var purpose = Mixed is null
                ? TenantId.HasValue ? HumanAdministratorCommitPurpose.RuntimeTenant : HumanAdministratorCommitPurpose.GlobalInstance
                : TenantId.HasValue ? HumanAdministratorCommitPurpose.MixedSettingsTenant : HumanAdministratorCommitPurpose.MixedSettingsInstance;
            if (released || context.ContextId != contextId || !connection.Matches(context)
                || opened && (connection.State != ConnectionState.Open || connection.BackendProcessId != backend
                    || connection.DataSource != dataSource || connection.Database != database)
                || context.TenantFilterTenantId != TenantId || context.IsTenantFilterBypassed
                || context.IsConfigurationInstanceImportOwner || !context.HasHumanCommitContextLease
                || !ReferenceEquals(context.HumanCommitAuditSubject, Subject)
                || !ReferenceEquals(selection.HumanScope?.Subject, Subject) || selection.HumanScope?.Purpose != purpose
                || (selection.PolicyPurpose ?? PolicyMutationPurpose.Standard)
                    != (Mixed is null ? PolicyMutationPurpose.Standard : PolicyMutationPurpose.MixedSettings)
                || Ordinary is { } ordinary && ordinary.Effects.Any(effect => effect.TenantId != TenantId)
                || Mixed is { } mixed && (mixed.TenantId != TenantId || !ReferenceEquals(mixed.Source.Subject, Subject)))
                throw new UnauthorizedAccessException("Federation source lost its original selected physical human owner.");
        }

        internal DbTransaction RequireTransaction()
        {
            RequireSelection();
            if (!opened || System.Transactions.Transaction.Current is not null
                || !context.HumanCommitAttemptOwnsTransaction
                || context.Database.CurrentTransaction?.GetDbTransaction() is not { } transaction
                || transaction.IsolationLevel != IsolationLevel.Serializable || !connection.Owns(transaction))
                throw new UnauthorizedAccessException("Federation writes require the original non-replaying Serializable attempt.");
            return transaction;
        }

        internal void RequireAcquired()
        {
            if (State != 1 || !ReferenceEquals(Transaction, RequireTransaction()))
                throw new UnauthorizedAccessException("The original acquired federation source is not active.");
        }

        internal void RequireEffectShape(HumanSettingsMutation effect)
        {
            if (!AtprotoFederationSettingPolicy.IsDedicatedKey(effect.Key) || effect.UserId is not null
                || effect.TenantId != TenantId
                || effect.Scope != (TenantId.HasValue ? SettingScope.Tenant : SettingScope.Instance)
                || (effect.Scope == SettingScope.Instance
                    ? effect.Operation != HumanSettingsOperation.InstanceSettings
                    : effect.Operation is not (HumanSettingsOperation.TenantSettings or HumanSettingsOperation.InstanceTenantSettings))
                || !Enum.IsDefined(effect.Kind)
                || effect.Kind == HumanSettingsMutationKind.SetValue && (effect.Value is null || effect.IsLocked.HasValue)
                || effect.Kind == HumanSettingsMutationKind.SetLock && (effect.Value is not null || !effect.IsLocked.HasValue)
                || effect.Kind == HumanSettingsMutationKind.Remove && (effect.Value is not null || effect.IsLocked.HasValue
                    || !AtprotoFederationSettingPolicy.CanReset(effect.Scope)))
                throw new UnauthorizedAccessException("A federation effect must match the exact original closed setting shape.");
            if (effect.Kind == HumanSettingsMutationKind.SetValue)
                AtprotoFederationSettingPolicy.Validate(effect.Key, effect.Value!, effect.Scope);
        }

        private void ConnectionChanged(object? sender, StateChangeEventArgs change)
        {
            if (change.CurrentState is ConnectionState.Closed or ConnectionState.Broken) released = true;
        }

        public void Dispose()
        {
            State = 2;
            released = true;
            connection.StateChange -= ConnectionChanged;
        }
    }

    private sealed record CompletedEffect(HumanSettingsMutation Effect,
        AtprotoFederationSettingImage? Before, AtprotoFederationSettingImage? After, bool Applied);
}
