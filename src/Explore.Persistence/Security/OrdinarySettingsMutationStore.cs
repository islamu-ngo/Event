using System.Data;
using System.Data.Common;
using System.Collections.Immutable;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Security;

/// <summary>
/// Ordinary Settings preimages and canonical writes on the original selected owner.
/// SQLite uses its write reservation; PostgreSQL uses the separately installed native
/// capability. A CLR scalar match or Serializable label does not replace source exclusion.
/// </summary>
public sealed class OrdinarySettingsMutationStore(
    ExploreDbContext context, IServiceProvider services, TimeProvider clock)
    : IOrdinarySettingsMutationStore, IDisposable
{
    private readonly ExploreDbContextExecutionLease selection = services.GetRequiredService<ExploreDbContextExecutionLease>();
    private readonly List<Source> sources = [];

    public async Task<IOrdinarySettingsSource> ObserveAsync(OrdinarySettingsMutation mutation,
        HumanCommitSubject subject, CancellationToken cancellationToken)
    {
        OrdinarySettingsSourceProviderQueries.RequireSupported(context);
        selection.RequireOriginalOwner(context);
        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
            || !ReferenceEquals(selection.HumanScope?.Subject, subject)
            || mutation.Effects.Any(effect => effect.TenantId != selection.SelectedTenantId))
            throw new UnauthorizedAccessException("Ordinary Settings observation requires its original idle human owner.");
        await context.Database.OpenConnectionAsync(cancellationToken);
        var native = await OrdinarySettingsSourceProviderQueries.ReadObservationAsync(context,
            services, selection, mutation, subject, clock, cancellationToken);
        var source = new Source(this, context, selection, mutation, subject, clock.GetUtcNow().UtcDateTime, native);
        sources.Add(source);
        try
        {
            source.RequireSelection();
            source.Expected = native?.Image ?? await ReadAsync(source, cancellationToken);
            source.Discovery = await Services.SettingsDiscoveryDisclosureObservation.ObserveAsync(
                context, selection, mutation, subject, cancellationToken);
            source.RequireSelection();
            return source;
        }
        catch { source.Dispose(); throw; }
    }

    public async Task AcquireAsync(IOrdinarySettingsSource source, CancellationToken cancellationToken)
    {
        var issued = RequireIssued(source);
        if (Interlocked.CompareExchange(ref issued.State, 1, 0) != 0)
            throw new UnauthorizedAccessException("An ordinary source belongs to one original physical attempt.");
        try
        {
            issued.Transaction = issued.RequireTransaction();
            // HumanSettingsMutationPreparation has already acquired PolicySourceSetFence.
            // SQLite's database-wide write reservation covers absent keys and parent locks,
            // not just rows returned by these reads, until this same transaction settles.
            if (issued.NativeObservation is not null)
                issued.NativeAttempt = await PostgresOrdinarySettingsContract.AcquireAsync(issued.NativeObservation, cancellationToken);
            else
                await RequireImageAsync(issued, cancellationToken);
            if (issued.Discovery is not null)
            {
                await issued.Discovery.BindAsync(cancellationToken);
                context.DisclosureMutations.EnlistSettingsSource(this, source);
            }
        }
        catch { issued.State = 2; throw; }
    }

    public async Task RequireCompleteAsync(IOrdinarySettingsSource source, CancellationToken cancellationToken)
    {
        var issued = RequireIssued(source);
        try
        {
            await RequireEffectsCompleteAsync(source, cancellationToken);
            if (issued.Discovery is not null)
                await context.DisclosureMutations.RequireSettingsCompleteAsync(this, source, cancellationToken);
        }
        catch { issued.State = 2; throw; }
    }

    private async Task RequireEffectsCompleteAsync(IOrdinarySettingsSource source, CancellationToken cancellationToken)
    {
        await VerifyAsync(source, cancellationToken);
        var issued = RequireIssued(source);
        if (issued.NativeAttempt is not null)
            await PostgresOrdinarySettingsContract.RequireCompleteAsync(issued.NativeAttempt, issued.Expected!, cancellationToken);
        else
            foreach (var effect in issued.Mutation.Effects.Where(effect => !AtprotoFederationSettingPolicy.IsDedicatedKey(effect.Key)))
                if (!issued.CompletedKeys.Contains(effect.Key) && !ProvesRefusal(issued, effect))
                    throw new UnauthorizedAccessException("An accepted ordinary effect has no private completion or excluded refusal.");
    }

    public async Task VerifyAsync(IOrdinarySettingsSource source, CancellationToken cancellationToken)
    {
        var issued = RequireIssued(source);
        try
        {
            issued.RequireAcquired();
            if (issued.Writing)
                throw new UnauthorizedAccessException("An ordinary writer has not privately completed.");
            await RequireImageAsync(issued, cancellationToken);
        }
        catch { issued.State = 2; throw; }
    }

    public void Expire(IOrdinarySettingsSource source) => RequireIssued(source).Dispose();

    public void Dispose()
    {
        foreach (var source in sources) source.Dispose();
    }

    internal async Task<SystemSetting?> ReadCurrentSystemAsync(
        IOrdinarySettingsSource source, string key, CancellationToken cancellationToken)
    {
        var issued = await RequireCurrentImageReadAsync(source, key, cancellationToken);
        return issued.Expected!.System[key]?.Entity();
    }

    internal async Task<TenantSetting?> ReadCurrentTenantAsync(
        IOrdinarySettingsSource source, Guid tenantId, string key, CancellationToken cancellationToken)
    {
        var issued = RequireIssued(source);
        try
        {
            issued.RequireAcquired();
            if (tenantId == Guid.Empty || issued.TenantId != tenantId)
                throw new UnauthorizedAccessException("An ordinary read cannot select another tenant.");
            await RequireCurrentImageReadAsync(source, key, cancellationToken);
            if (!issued.Expected!.Tenant.TryGetValue(key, out var row))
                throw new UnauthorizedAccessException("The original ordinary tenant image does not contain this key.");
            return row?.Entity();
        }
        catch { issued.State = 2; throw; }
    }

    private async Task<Source> RequireCurrentImageReadAsync(
        IOrdinarySettingsSource source, string key, CancellationToken token)
    {
        var issued = RequireIssued(source);
        try
        {
            token.ThrowIfCancellationRequested();
            RequireActive();
            if (AtprotoFederationSettingPolicy.IsDedicatedKey(key)
                || !issued.Keys.Contains(key, StringComparer.Ordinal)
                || !issued.Expected!.System.ContainsKey(key))
                throw new UnauthorizedAccessException("An ordinary read requires its original captured key.");
            await VerifyAsync(source, token);
            RequireActive();
            return issued;
        }
        catch { issued.State = 2; throw; }

        void RequireActive()
        {
            issued.RequireAcquired();
            if (!ReferenceEquals(services.GetRequiredService<IHumanSettingsMutationPreparation>()
                    .CurrentOrdinarySource, source))
                throw new UnauthorizedAccessException("Ordinary reads require their original issuer's active callback.");
        }
    }

    public async Task<string?> WriteSystemAsync(IOrdinarySettingsSource source, SystemSetting candidate,
        HumanSettingsMutationKind kind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var input = OrdinarySystemSettingImage.From(candidate);
        var issued = await BeginWriteAsync(source, SettingScope.Instance, null, null, input.Key,
            kind, kind == HumanSettingsMutationKind.SetValue ? input.Value : null,
            kind == HumanSettingsMutationKind.SetLock ? input.Locked : null, cancellationToken);
        try
        {
            var before = issued.Expected!.System[input.Key];
            if (kind == HumanSettingsMutationKind.SetLock && before is null)
                throw new UnauthorizedAccessException("Ordinary locking cannot create a missing system setting.");
            if (kind == HumanSettingsMutationKind.SetValue && input.Locked != (before?.Locked ?? false))
                throw new UnauthorizedAccessException("An ordinary value cannot change the original lock.");
            var definition = SettingRegistry.Get(input.Key)!;
            var after = before is null
                ? new OrdinarySystemSettingImage(issued.NewIds[input.Key], input.Key,
                    input.Value, (int)definition.ValueType, false, null, definition.Description,
                    definition.Category, 0, issued.OccurredAt, issued.Subject.PrincipalId, null, null)
                : before with
                {
                    Value = kind == HumanSettingsMutationKind.SetValue ? input.Value : before.Value,
                    Locked = kind == HumanSettingsMutationKind.SetLock ? input.Locked : before.Locked,
                    UpdatedAt = issued.OccurredAt,
                    UpdatedBy = issued.Subject.PrincipalId
                };
            if (issued.NativeAttempt is not null)
            {
                var effect = await PostgresOrdinarySettingsContract.WriteSystemAsync(issued.NativeAttempt,
                    input.Key, kind, before, after, cancellationToken);
                if (effect.Before != before || effect.After != after)
                    throw new UnauthorizedAccessException("The native system writer substituted its canonical row transition.");
                after = effect.After!;
            }
            else if (before is null)
                await InsertAsync(issued, after.Entity(), cancellationToken);
            else
                await context.SystemSettings.Where(row => row.Id == before.Id && row.SettingKey == before.Key)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, after.Value)
                        .SetProperty(row => row.IsLocked, after.Locked)
                        .SetProperty(row => row.UpdatedAt, after.UpdatedAt)
                        .SetProperty(row => row.UpdatedBy, after.UpdatedBy), cancellationToken);
            if (issued.NativeAttempt is not null || before is not null)
                await ManagedTenantProvisioningPolicyProjection.RecordAsync(
                    context, input.Key, after.Entity(), cancellationToken);
            await CompleteWriteAsync(issued, issued.Expected with
            {
                System = issued.Expected.System.SetItem(input.Key, after)
            }, cancellationToken);
            RecordDisclosureEffect(issued);
            return before?.Value;
        }
        catch { issued.State = 2; throw; }
    }

    public async Task<bool> WriteTenantAsync(IOrdinarySettingsSource source, Guid tenantId, string key,
        HumanSettingsMutationKind kind, string? value, bool? isLocked, CancellationToken cancellationToken)
    {
        var issued = await BeginWriteAsync(source, SettingScope.Tenant, tenantId, null, key,
            kind, value, isLocked, cancellationToken);
        try
        {
            var before = issued.Expected!.Tenant[key];
            if (issued.Expected.System[key]?.Locked == true)
                throw new SettingSystemLockedException(key);
            if (kind == HumanSettingsMutationKind.SetValue
                && issued.Mutation.RequiresTenantVerificationCeiling
                && key == GovernanceSettingKeys.Organizations.VerificationRequired
                && value == "false"
                && (!bool.TryParse(issued.Expected.System[GovernanceSettingKeys.Organizations.TenantCanOmitVerification]?.Value,
                    out var canOmit) || !canOmit))
                throw new UnauthorizedAccessException("The original organization-verification ceiling forbids this value.");
            OrdinaryTenantSettingImage? after = before;
            bool changed;
            if (kind == HumanSettingsMutationKind.Remove)
            {
                changed = before is not null;
                if (before is not null && issued.NativeAttempt is null)
                    await context.Set<TenantSetting>().Where(row => row.Id == before.Id && row.TenantId == tenantId)
                        .ExecuteDeleteAsync(cancellationToken);
                after = null;
            }
            else if (kind == HumanSettingsMutationKind.SetLock)
            {
                changed = before is not null && before.Locked != isLocked;
                if (changed)
                {
                    after = before! with { Locked = isLocked!.Value, UpdatedAt = issued.OccurredAt, UpdatedBy = issued.Subject.PrincipalId };
                    if (issued.NativeAttempt is null)
                        await context.Set<TenantSetting>().Where(row => row.Id == before!.Id && row.TenantId == tenantId)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsLocked, after.Locked)
                                .SetProperty(row => row.UpdatedAt, after.UpdatedAt)
                                .SetProperty(row => row.UpdatedBy, after.UpdatedBy), cancellationToken);
                }
            }
            else
            {
                changed = true;
                after = before is null
                    ? new(issued.NewIds[key], tenantId, key, value!, false,
                        issued.OccurredAt, issued.Subject.PrincipalId, null, null)
                    : before with { Value = value!, UpdatedAt = issued.OccurredAt, UpdatedBy = issued.Subject.PrincipalId };
                if (issued.NativeAttempt is null)
                {
                    if (before is null) await InsertAsync(issued, after.Entity(), cancellationToken);
                    else
                        await context.Set<TenantSetting>().Where(row => row.Id == before.Id && row.TenantId == tenantId)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, after.Value)
                                .SetProperty(row => row.UpdatedAt, after.UpdatedAt)
                                .SetProperty(row => row.UpdatedBy, after.UpdatedBy), cancellationToken);
                }
            }
            if (issued.NativeAttempt is not null)
            {
                var effect = await PostgresOrdinarySettingsContract.WriteTenantAsync(issued.NativeAttempt,
                    key, kind, before, after, cancellationToken);
                if (effect.Before != before || effect.After != after)
                    throw new UnauthorizedAccessException("The native tenant writer substituted its canonical row transition.");
                after = effect.After;
            }
            await CompleteWriteAsync(issued, issued.Expected with { Tenant = issued.Expected.Tenant.SetItem(key, after) },
                cancellationToken);
            if (changed) RecordDisclosureEffect(issued);
            return changed;
        }
        catch { issued.State = 2; throw; }
    }

    public async Task<UserPreference?> WriteUserAsync(IOrdinarySettingsSource source, Guid tenantId,
        Guid userId, string key, OrdinaryUserPreferenceWrite operation, string? value, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(operation))
            throw new UnauthorizedAccessException("An ordinary preference requires its exact repository operation.");
        var kind = operation == OrdinaryUserPreferenceWrite.Remove
            ? HumanSettingsMutationKind.Remove : HumanSettingsMutationKind.SetValue;
        var issued = await BeginWriteAsync(source, SettingScope.User, tenantId, userId, key, kind, value, null,
            cancellationToken);
        try
        {
            var before = issued.Expected!.User[key];
            if (operation == OrdinaryUserPreferenceWrite.Create && before is not null
                || operation == OrdinaryUserPreferenceWrite.Update && before is null)
                throw new UnauthorizedAccessException("The original preference presence does not match this operation.");
            OrdinaryUserPreferenceImage? after;
            if (kind == HumanSettingsMutationKind.Remove)
            {
                if (before is not null && issued.NativeAttempt is null)
                    await context.UserPreferences.Where(row => row.Id == before.Id && row.TenantId == tenantId && row.UserId == userId)
                        .ExecuteDeleteAsync(cancellationToken);
                after = null;
            }
            else
            {
                if (issued.Expected.System[key]?.Locked == true || issued.Expected.Tenant[key]?.Locked == true)
                    throw new UnauthorizedAccessException("The original higher-scope lock forbids this preference.");
                after = before is null
                    ? new(issued.NewIds[key], tenantId, userId, key, value!, issued.OccurredAt, issued.Subject.PrincipalId, null, null)
                    : before with { Value = value!, UpdatedAt = issued.OccurredAt, UpdatedBy = issued.Subject.PrincipalId };
                if (issued.NativeAttempt is null)
                {
                    if (before is null) await InsertAsync(issued, after.Entity(), cancellationToken);
                    else
                        await context.UserPreferences.Where(row => row.Id == before.Id && row.TenantId == tenantId && row.UserId == userId)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, after.Value)
                                .SetProperty(row => row.UpdatedAt, after.UpdatedAt)
                                .SetProperty(row => row.UpdatedBy, after.UpdatedBy), cancellationToken);
                }
            }
            if (issued.NativeAttempt is not null)
            {
                var effect = await PostgresOrdinarySettingsContract.WriteUserAsync(issued.NativeAttempt,
                    key, operation, before, after, cancellationToken);
                if (effect.Before != before || effect.After != after)
                    throw new UnauthorizedAccessException("The native preference writer substituted its canonical row transition.");
                after = effect.After;
            }
            await CompleteWriteAsync(issued, issued.Expected with { User = issued.Expected.User.SetItem(key, after) },
                cancellationToken);
            return (kind == HumanSettingsMutationKind.Remove ? before : after)?.Entity();
        }
        catch { issued.State = 2; throw; }
    }

    private async Task<Source> BeginWriteAsync(IOrdinarySettingsSource source, SettingScope scope,
        Guid? tenantId, Guid? userId, string key, HumanSettingsMutationKind kind, string? value, bool? locked,
        CancellationToken token)
    {
        var issued = RequireIssued(source);
        try
        {
            var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
            if (!ReferenceEquals(preparation.CurrentOrdinarySource, source))
                throw new UnauthorizedAccessException("Ordinary writes require their original issuer's active callback.");
            await preparation.RequireCurrentOwnedAsync(scope, tenantId, userId, key, kind, value, locked, token);
            issued.RequireAcquired();
            if (!issued.Mutation.Effects.Any(effect => effect.Scope == scope && effect.TenantId == tenantId
                && effect.UserId == userId && effect.Key == key && effect.Kind == kind && effect.Value == value
                && effect.IsLocked == locked) || AtprotoFederationSettingPolicy.IsDedicatedKey(key)
                || issued.Writing || !issued.CompletedKeys.Add(key))
                throw new UnauthorizedAccessException("An ordinary effect must be original and privately applied once.");
            if (context.ChangeTracker.Entries().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
                throw new UnauthorizedAccessException("Ordinary Settings cannot flush caller-owned tracked mutations.");
            issued.Writing = true;
            issued.PendingEffect = issued.Mutation.Effects.Single(effect => effect.Key == key);
            return issued;
        }
        catch { issued.State = 2; throw; }
    }

    private async Task InsertAsync<TEntity>(Source source, TEntity row, CancellationToken token) where TEntity : class
    {
        source.PendingEntity = row;
        context.Add(row);
        try { await context.SaveChangesAsync(token); }
        finally
        {
            context.Entry(row).State = EntityState.Detached;
            source.PendingEntity = null;
        }
    }

    private async Task CompleteWriteAsync(Source source, OrdinarySettingsDataImage expected, CancellationToken token)
    {
        // Advance only from the canonical effect, never by accepting the database's observed
        // after-image as the expected outcome. This slot is inaccessible to repository callers.
        source.RequireAcquired();
        if (source.NativeAttempt is not null)
            await PostgresOrdinarySettingsContract.VerifyAsync(source.NativeAttempt, expected, token);
        else if (!expected.Same(await ReadAsync(source, token)))
            throw new UnauthorizedAccessException("The ordinary writer did not persist its exact canonical effect.");
        source.RequireAcquired();
        source.Expected = expected;
        source.Writing = false;
    }

    private async Task RequireImageAsync(Source source, CancellationToken token)
    {
        source.RequireAcquired();
        if (source.Expected is null)
            throw new UnauthorizedAccessException("Original ordinary Settings rows, absences or private effects changed.");
        if (source.NativeAttempt is not null)
            await PostgresOrdinarySettingsContract.VerifyAsync(source.NativeAttempt, source.Expected, token);
        else if (!source.Expected.Same(await ReadAsync(source, token)))
            throw new UnauthorizedAccessException("Original ordinary Settings rows, absences or private effects changed.");
        source.RequireAcquired();
    }

    private Task<OrdinarySettingsDataImage> ReadAsync(Source source, CancellationToken token) =>
        OrdinarySettingsDataImage.ReadAsync(context, source.Keys, source.TenantId,
            source.Mutation.Effects.Any(effect => effect.Scope == SettingScope.User) ? source.Subject.PrincipalId : null, token);

    private Source RequireIssued(IOrdinarySettingsSource source) =>
        source is Source issued && ReferenceEquals(issued.Store, this) ? issued
            : throw new UnauthorizedAccessException("The original selected ordinary store must issue this source.");

    internal Services.SettingsDiscoveryDisclosureObservation RequireDiscoveryObservation(IOrdinarySettingsSource source)
    {
        var issued = RequireIssued(source);
        issued.RequireAcquired();
        return issued.Discovery ?? throw new UnauthorizedAccessException("The original ordinary discovery observation is missing.");
    }

    internal bool OwnsPendingDiscoveryRow(IOrdinarySettingsSource source, object entity)
    {
        var issued = RequireIssued(source);
        issued.RequireAcquired();
        return issued.Writing && ReferenceEquals(issued.PendingEntity, entity);
    }

    internal async Task<ImmutableArray<HumanSettingsMutation>> RequireDisclosureEffectsAsync(IOrdinarySettingsSource source,
        CancellationToken token)
    {
        await RequireEffectsCompleteAsync(source, token);
        return RequireIssued(source).DisclosureEffects.ToImmutableArray();
    }

    private static void RecordDisclosureEffect(Source source)
    {
        var effect = source.PendingEffect ?? throw new UnauthorizedAccessException("The original private ordinary effect is missing.");
        if (effect.Scope != SettingScope.User && Services.EventDiscoveryDisclosureMutationScope.IsDisclosureSetting(effect.Key))
            source.DisclosureEffects.Add(effect);
    }

    private static bool ProvesRefusal(Source source, HumanSettingsMutation effect)
    {
        var image = source.Expected!;
        bool absent = effect.Scope == SettingScope.Instance ? image.System.GetValueOrDefault(effect.Key) is null
            : effect.Scope == SettingScope.Tenant ? image.Tenant.GetValueOrDefault(effect.Key) is null
            : image.User.GetValueOrDefault(effect.Key) is null;
        return effect.Kind == HumanSettingsMutationKind.SetValue && effect.Scope != SettingScope.Instance
                && (image.System.GetValueOrDefault(effect.Key)?.Locked == true
                    || effect.Scope == SettingScope.User && image.Tenant.GetValueOrDefault(effect.Key)?.Locked == true)
            || effect.Kind == HumanSettingsMutationKind.Remove && absent
            || effect.Kind == HumanSettingsMutationKind.SetLock && (absent
                || effect.Scope == SettingScope.Tenant && image.Tenant.GetValueOrDefault(effect.Key)?.Locked == effect.IsLocked)
            || effect.Kind == HumanSettingsMutationKind.SetValue && source.Mutation.RequiresTenantVerificationCeiling
                && effect.Key == GovernanceSettingKeys.Organizations.VerificationRequired && effect.Value == "false"
                && (!bool.TryParse(image.System.GetValueOrDefault(GovernanceSettingKeys.Organizations.TenantCanOmitVerification)?.Value,
                    out bool canOmit) || !canOmit);
    }

    private sealed class Source : IOrdinarySettingsSource, IDisposable
    {
        private readonly ExploreDbContext context;
        private readonly ExploreDbContextExecutionLease selection;
        private readonly RelationalConnectionIdentity connection;
        private readonly DbContextId contextId;
        private bool released;

        internal Source(OrdinarySettingsMutationStore store, ExploreDbContext context,
            ExploreDbContextExecutionLease selection, OrdinarySettingsMutation mutation,
            HumanCommitSubject subject, DateTime occurredAt, PostgresOrdinarySettingsContract.IObservation? native)
        {
            Store = store;
            this.context = context;
            this.selection = selection;
            Mutation = mutation;
            Subject = subject;
            NativeObservation = native;
            OccurredAt = native?.OccurredAt ?? occurredAt;
            TenantId = selection.SelectedTenantId;
            connection = RelationalConnectionIdentity.Capture(context);
            contextId = context.ContextId;
            Keys = OrdinarySettingsDataImage.Keys(mutation);
            NewIds = native?.NewIds.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                ?? mutation.Effects.Where(effect => !AtprotoFederationSettingPolicy.IsDedicatedKey(effect.Key))
                    .ToDictionary(effect => effect.Key, _ => Guid.CreateVersion7(), StringComparer.Ordinal);
            connection.StateChange += ConnectionChanged;
        }

        internal OrdinarySettingsMutationStore Store { get; }
        internal OrdinarySettingsMutation Mutation { get; }
        internal HumanCommitSubject Subject { get; }
        internal Guid? TenantId { get; }
        internal DateTime OccurredAt { get; }
        internal string[] Keys { get; }
        internal Dictionary<string, Guid> NewIds { get; }
        internal HashSet<string> CompletedKeys { get; } = new(StringComparer.Ordinal);
        internal OrdinarySettingsDataImage? Expected { get; set; }
        internal Services.SettingsDiscoveryDisclosureObservation? Discovery { get; set; }
        internal List<HumanSettingsMutation> DisclosureEffects { get; } = [];
        internal HumanSettingsMutation? PendingEffect { get; set; }
        internal object? PendingEntity { get; set; }
        internal PostgresOrdinarySettingsContract.IObservation? NativeObservation { get; }
        internal PostgresOrdinarySettingsContract.IAttempt? NativeAttempt { get; set; }
        internal DbTransaction? Transaction { get; set; }
        internal bool Writing { get; set; }
        internal int State;

        internal void RequireSelection()
        {
            selection.RequireOriginalOwner(context);
            if (released || context.ContextId != contextId || context.TenantFilterTenantId != TenantId
                || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
                || (selection.PolicyPurpose ?? PolicyMutationPurpose.Standard) != PolicyMutationPurpose.Standard
                || !ReferenceEquals(selection.HumanScope?.Subject, Subject)
                || !ReferenceEquals(context.HumanCommitAuditSubject, Subject)
                || !context.HasHumanCommitContextLease || !connection.Matches(context)
                || connection.State != ConnectionState.Open
                || selection.HumanScope?.Purpose != (TenantId.HasValue
                    ? HumanAdministratorCommitPurpose.RuntimeTenant : HumanAdministratorCommitPurpose.GlobalInstance))
                throw new UnauthorizedAccessException("Ordinary Settings lost the original selected physical human owner.");
        }

        internal DbTransaction RequireTransaction()
        {
            RequireSelection();
            if (System.Transactions.Transaction.Current is not null || !context.HumanCommitAttemptOwnsTransaction
                || context.Database.CurrentTransaction?.GetDbTransaction() is not { } transaction
                || transaction.IsolationLevel != IsolationLevel.Serializable
                || !connection.Owns(transaction))
                throw new UnauthorizedAccessException("Ordinary Settings requires its non-replaying physical transaction.");
            return transaction;
        }

        internal void RequireAcquired()
        {
            if (State != 1 || !ReferenceEquals(Transaction, RequireTransaction()))
                throw new UnauthorizedAccessException("The original ordinary source is not active.");
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
            NativeObservation?.Dispose();
        }
    }
}
