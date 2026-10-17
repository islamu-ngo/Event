using System.Collections.Immutable;
using System.Data;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Services;

/// <summary>
/// Fixed resolver scalar writer. Only the original ordinary Global human owner may apply
/// its compiled leaves; no setup, import, operator-editor or generic repository grant is used.
/// </summary>
public sealed class ResolverConfigurationSettingsWriter(
    ExploreDbContext context, IUnitOfWork unitOfWork, IServiceProvider services,
    IResolverConfigurationMutationAuthority authority, IHumanCommitScopePreparation human,
    IHumanSettingsMutationPreparation preparation) : IResolverConfigurationSettingsWriter, IDisposable
{
    private IResolverConfigurationMutationSource? selected;
    private ImmutableDictionary<string, ResolverConfigurationSettingSnapshot?>? observed;
    private Action? requireSelection;
    private ResolverConfigurationMutation? applied;
    private Func<CancellationToken, Task>? requireApplied;
    private PostgresNativeQueueEnrollment? nativeEnrollment;
    private DateTime? representedTimestamp;
    private RelationalConnectionIdentity? originalNativeConnection;
    private bool connectionReleased;

    private void OnConnectionStateChange(object? sender, StateChangeEventArgs change)
    {
        if (change.CurrentState is ConnectionState.Closed or ConnectionState.Broken) connectionReleased = true;
    }

    public void Dispose()
    {
        connectionReleased = true;
        if (originalNativeConnection is not null)
            originalNativeConnection.StateChange -= OnConnectionStateChange;
    }

    public DateTime NormalizeEffectTimestamp(DateTime timestamp)
    {
        if (selected is null || requireSelection is null || context.Database.CurrentTransaction is not null
            || representedTimestamp.HasValue || timestamp.Kind != DateTimeKind.Utc)
            throw new UnauthorizedAccessException("Resolver timestamp representation requires its original idle observation.");
        requireSelection();
        authority.RequireIssued(selected);
        selected.RequireTime();
        // Only persisted data is represented. Signed credentials and support horizons retain
        // their original full precision, including the terminal source's deadline comparison.
        representedTimestamp = nativeEnrollment is null
            ? timestamp : new DateTime(timestamp.Ticks - timestamp.Ticks % 10, DateTimeKind.Utc);
        return representedTimestamp.Value;
    }

    public async Task<ImmutableDictionary<string, ResolverConfigurationSettingSnapshot?>> ObserveAsync(
        IResolverConfigurationMutationSource source, IUnitOfWork originalOwner, CancellationToken cancellationToken)
    {
        authority.RequireIssued(source);
        if (selected is not null || context.Database.CurrentTransaction is not null
            || !ReferenceEquals(originalOwner, unitOfWork) || unitOfWork is not EfCoreUnitOfWork original)
            throw new UnauthorizedAccessException("Resolver observation requires its idle original Global owner.");
        if (!(RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.Sqlite) && !(RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql))
            throw new NotSupportedException("resolver_configuration_native_custody_not_supported");
        original.RequireTrustedSetupSettingsOwner(context);
        var lease = services.GetRequiredService<ExploreDbContextExecutionLease>();
        var contextId = context.ContextId;
        var connection = RelationalConnectionIdentity.Capture(context);
        int? backend = null;
        var subject = human.Subject;
        requireSelection = () =>
        {
            lease.RequireOriginalOwner(context);
            if (context.ContextId != contextId || context.TenantFilterTenantId is not null
                || lease.SelectedTenantId is not null
                || (lease.PolicyPurpose ?? PolicyMutationPurpose.Standard) != PolicyMutationPurpose.Standard
                || context.IsConfigurationInstanceImportOwner || context.IsTenantFilterBypassed
                || !connection.Matches(context)
                || backend is not null && (connectionReleased || connection.State != ConnectionState.Open
                    || connection.BackendProcessId != backend)
                || subject is null || subject.PrincipalId != source.Actor
                || !ReferenceEquals(subject, source.Subject) || !ReferenceEquals(lease.HumanLifetime, source.Lifetime)
                || !ReferenceEquals(human.Subject, subject) || !ReferenceEquals(lease.HumanScope?.Subject, subject)
                || lease.HumanScope?.Purpose != HumanAdministratorCommitPurpose.GlobalInstance)
                throw new UnauthorizedAccessException("Resolver mutation lost its original ordinary Global human owner.");
        };
        requireSelection();
        selected = source;
        await source.RequireSourcesAsync(cancellationToken);
        // Keep this exact connection open through observation, acquisition and settlement.
        // No source exclusion or database transaction is held during original authentication.
        await context.Database.OpenConnectionAsync(cancellationToken);
        if ((RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql))
        {
            backend = connection.PostgresProcessId;
            originalNativeConnection = connection;
            originalNativeConnection.StateChange += OnConnectionStateChange;
            nativeEnrollment = PostgresNativeQueueEnrollment.Bind(services.GetRequiredService<IConfiguration>());
        }
        observed = await ReadAsync(cancellationToken);
        requireSelection();
        await source.RequireSourcesAsync(cancellationToken);
        source.RequireTime();
        return observed;
    }

    public async Task ExecuteAsync(ResolverConfigurationMutation mutation, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(selected, mutation.Source) || requireSelection is null || observed is null
            || !ReferenceEquals(unitOfWork, mutation.OriginalOwner) || !ReferenceEquals(observed, mutation.Before)
            || nativeEnrollment is not null && (representedTimestamp is null
                || mutation.Effects.Any(effect => effect.UpdatedAt != representedTimestamp)))
            throw new UnauthorizedAccessException("Resolver effects require their original observed source.");
        mutation.ClaimOnce();
        authority.RequireIssued(mutation.Source);
        var connection = RelationalConnectionIdentity.Capture(context);
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new UnauthorizedAccessException("Resolver effects require their original physical transaction.");
        void RequireOwner()
        {
            requireSelection();
            mutation.RequireActive();
            if (!ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), transaction)
                || connection.State != ConnectionState.Open
                || !connection.Owns(transaction)
                || transaction.IsolationLevel != IsolationLevel.Serializable
                || !context.HumanCommitAttemptOwnsTransaction)
                throw new UnauthorizedAccessException("Resolver mutation requires one non-replaying Serializable attempt.");
        }
        RequireOwner();
        await mutation.Source.RequireSourcesAsync(cancellationToken);
        foreach (var effect in mutation.Effects)
            await preparation.RequireCurrentOwnedAsync(SettingScope.Instance, null, null, effect.Key,
                HumanSettingsMutationKind.SetValue, effect.Value, cancellationToken: cancellationToken);
        var current = await ReadAsync(cancellationToken);
        if (ResolverConfigurationMutation.Keys.Any(key => current[key] != observed[key]))
            throw new UnauthorizedAccessException("The original resolver validation source changed before acquisition.");
        if (context.ChangeTracker.Entries().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            || context.ChangeTracker.Entries<SystemSetting>().Any(entry =>
                observed.TryGetValue(entry.Entity.SettingKey, out var row)
                && ResolverConfigurationSettingSnapshot.From(entry.Entity) != row))
            throw new UnauthorizedAccessException("Resolver mutation cannot flush unrelated or substituted tracked state.");
        if (nativeEnrollment is not null)
        {
            RequireOwner();
            await PostgresResolverConfigurationContract.WriteAsync(context, nativeEnrollment, mutation, cancellationToken);
        }
        else
        {
            foreach (var effect in mutation.Effects)
            {
                RequireOwner();
                var row = await context.SystemSettings.SingleOrDefaultAsync(
                    item => item.SettingKey == effect.Key, cancellationToken);
                if (row is null)
                {
                    row = new SystemSetting
                    {
                        Id = effect.Id,
                        SettingKey = effect.Key,
                        Value = effect.Value,
                        CreatedAt = effect.CreatedAt,
                        CreatedBy = effect.CreatedBy
                    };
                    context.SystemSettings.Add(row);
                }
                row.Value = effect.Value;
                row.ValueType = effect.ValueType;
                row.IsLocked = effect.IsLocked;
                row.AllowedValues = effect.AllowedValues;
                row.Description = effect.Description;
                row.Category = effect.Category;
                row.DisplayOrder = effect.DisplayOrder;
                row.UpdatedAt = effect.UpdatedAt;
                row.UpdatedBy = effect.UpdatedBy;
            }
            await context.SaveChangesAsync(cancellationToken);
        }
        var after = observed.SetItems(mutation.Effects.Select(effect =>
            KeyValuePair.Create<string, ResolverConfigurationSettingSnapshot?>(effect.Key, effect)));
        async Task RequireFinalSources(CancellationToken token)
        {
            RequireOwner();
            await mutation.Source.RequireSourcesAsync(token);
            var actual = await ReadAsync(token);
            // The unpatched leaves are also the original validation projection. Compare all
            // scalar columns, including IDs, preserved creation metadata and current audit actor.
            if (ResolverConfigurationMutation.Keys.Any(key => actual[key] != after[key]))
                throw new UnauthorizedAccessException("The exact resolver effects, projection or audit columns changed.");
            RequireOwner();
        }
        await RequireFinalSources(cancellationToken);
        mutation.Source.RequireTime();
        applied = mutation;
        requireApplied = RequireFinalSources;
        // The closed human preparation already installed mandatory completion admission.
        // Skipping this writer cannot remove the final effect check from the physical owner.
    }

    public Task RequireAppliedAsync(ResolverConfigurationMutation mutation, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(applied, mutation) || requireApplied is null)
            throw new UnauthorizedAccessException("The original fixed resolver writer did not apply this capsule.");
        return requireApplied(cancellationToken);
    }

    private async Task<ImmutableDictionary<string, ResolverConfigurationSettingSnapshot?>> ReadAsync(CancellationToken token)
    {
        if (nativeEnrollment is not null)
        {
            requireSelection!();
            var native = await PostgresResolverConfigurationContract.ReadAsync(context, nativeEnrollment, token);
            requireSelection();
            return native;
        }
        var keys = ResolverConfigurationMutation.Keys.ToArray();
        var rows = await context.SystemSettings.AsNoTracking().Where(row => keys.Contains(row.SettingKey))
            .ToListAsync(token);
        return keys.ToImmutableDictionary(key => key, key =>
        {
            var row = rows.SingleOrDefault(item => item.SettingKey == key);
            return row is null ? null : ResolverConfigurationSettingSnapshot.From(row);
        }, StringComparer.Ordinal);
    }
}
