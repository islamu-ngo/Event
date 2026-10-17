using System.Collections.Immutable;
using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Secrets;
using Explore.Domain.Settings;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Persistence.Services;

/// <summary>Fixed configuration data writer on one retained physical owner, never a generic scalar permit.</summary>
public sealed class StorageConfigurationWriter(ExploreDbContext context, IUnitOfWork unit,
    IServiceProvider services, IStorageConfigurationAuthority authority,
    IHumanCommitScopePreparation human, IHumanSettingsMutationPreparation preparation)
    : IStorageConfigurationWriter, IDisposable
{
    private IStorageConfigurationSource? selected;
    private StorageConfigurationState? observed;
    private Action? requireSelection;
    private PostgresNativeQueueEnrollment? enrollment;
    private RelationalConnectionIdentity? connection;
    private bool released;
    private DateTime? timestamp;
    private StorageSettingsMutation? applied;
    private Func<CancellationToken, Task>? completion;
    private int started;

    private void OnState(object? sender, StateChangeEventArgs change)
    {
        if (change.CurrentState is ConnectionState.Closed or ConnectionState.Broken) released = true;
    }

    public void Dispose()
    {
        released = true;
        if (connection is not null) connection.StateChange -= OnState;
    }

    public void RequireIdle()
    {
        if (selected is null || requireSelection is null || context.Database.CurrentTransaction is not null)
            throw new UnauthorizedAccessException("Configuration external work requires its original idle owner.");
        requireSelection();
    }

    public DateTime RepresentTimestamp(DateTime value)
    {
        RequireIdle();
        if (timestamp.HasValue || value.Kind != DateTimeKind.Utc)
            throw new UnauthorizedAccessException("Configuration data time is represented once before compilation.");
        timestamp = enrollment is null ? value : new DateTime(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);
        return timestamp.Value;
    }

    public async Task<StorageConfigurationState> ObserveAsync(
        IStorageConfigurationSource source, IUnitOfWork owner, CancellationToken token)
    {
        authority.RequireIssued(source);
        if (selected is not null || !ReferenceEquals(unit, owner) || unit is not EfCoreUnitOfWork actual
            || context.Database.CurrentTransaction is not null)
            throw new UnauthorizedAccessException("Configuration observation requires its original idle unit.");
        if (!(RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.Sqlite) && !(RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql))
            throw new NotSupportedException("storage_configuration_native_custody_not_supported");
        // This existing method checks only exact unit/context identity; it grants no setup authority.
        actual.RequireTrustedSetupSettingsOwner(context);
        var lease = services.GetRequiredService<ExploreDbContextExecutionLease>();
        var contextId = context.ContextId;
        connection = RelationalConnectionIdentity.Capture(context);
        var originalConnection = connection;
        int? backend = null;
        selected = source;
        requireSelection = () =>
        {
            lease.RequireOriginalOwner(context);
            authority.RequireIssued(source);
            if (released || context.ContextId != contextId || context.TenantFilterTenantId != source.TenantId
                || lease.SelectedTenantId != source.TenantId
                || (lease.PolicyPurpose ?? PolicyMutationPurpose.Standard) != PolicyMutationPurpose.Standard
                || context.IsConfigurationInstanceImportOwner || context.IsTenantFilterBypassed
                || !originalConnection.Matches(context)
                || backend.HasValue && (originalConnection.State != ConnectionState.Open
                    || originalConnection.BackendProcessId != backend)
                || !ReferenceEquals(human.Subject, source.Subject)
                || !ReferenceEquals(lease.HumanLifetime, source.Lifetime)
                || !ReferenceEquals(lease.HumanScope?.Subject, source.Subject)
                || lease.HumanScope?.Purpose != (source.TenantId.HasValue
                    ? HumanAdministratorCommitPurpose.RuntimeTenant : HumanAdministratorCommitPurpose.GlobalInstance))
                throw new UnauthorizedAccessException("Configuration lost its original context, purpose, backend or human.");
        };
        requireSelection();
        await source.RequireSourcesAsync(token);
        await context.Database.OpenConnectionAsync(token);
        connection.StateChange += OnState;
        if ((RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql))
        {
            backend = connection.PostgresProcessId;
            enrollment = PostgresNativeQueueEnrollment.Bind(services.GetRequiredService<IConfiguration>());
        }
        observed = await ReadAsync(token);
        await source.RequireSourcesAsync(token);
        source.RequireTime();
        return observed;
    }

    public async Task RequireUnchangedAsync(StorageConfigurationState original, CancellationToken token)
    {
        RequireIdle();
        if (!ReferenceEquals(original, observed) || !original.Same(await ReadAsync(token)))
            throw new UnauthorizedAccessException("The first storage configuration sources changed.");
        await selected!.RequireSourcesAsync(token);
        selected.RequireTime();
    }

    public async Task ExecuteAsync(StorageSettingsMutation mutation, CancellationToken token)
    {
        mutation.RequireActive();
        if (Interlocked.Exchange(ref started, 1) != 0 || !ReferenceEquals(selected, mutation.Source)
            || !ReferenceEquals(unit, mutation.OriginalOwner) || !ReferenceEquals(observed, mutation.Before)
            || timestamp != mutation.OccurredAt || mutation.Effects.IsEmpty
            || mutation.Effects.Any(row => row.UpdatedAt != timestamp))
            throw new UnauthorizedAccessException("Configuration must use its exact original observation and represented effects.");
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new UnauthorizedAccessException("Configuration requires its original physical transaction.");
        void RequireOwner()
        {
            requireSelection!();
            mutation.RequireActive();
            if (!ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), transaction)
                || !connection!.Owns(transaction)
                || connection!.State != ConnectionState.Open || transaction.IsolationLevel != IsolationLevel.Serializable
                || !context.HumanCommitAttemptOwnsTransaction)
                throw new UnauthorizedAccessException("Configuration requires one non-replaying Serializable attempt.");
        }
        RequireOwner();
        var validation = await mutation.ValidateAsync(token);
        if (!validation.IsValid) throw new FluentValidation.ValidationException(validation.Errors);
        await mutation.Source.RequireSourcesAsync(token);
        foreach (var row in mutation.Effects)
        {
            await preparation.RequireCurrentOwnedAsync(row.TenantId.HasValue ? SettingScope.Tenant : SettingScope.Instance,
                row.TenantId, null, row.Key, HumanSettingsMutationKind.SetValue, row.Value, cancellationToken: token);
            if (row.TenantId is null)
                await preparation.RequireCurrentOwnedAsync(SettingScope.Instance, null, null, row.Key,
                    HumanSettingsMutationKind.SetLock, isLocked: false, cancellationToken: token);
        }
        if (!observed!.Same(await ReadAsync(token)))
            throw new UnauthorizedAccessException("The first validation source changed before acquisition.");
        if (context.ChangeTracker.Entries().Any(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            throw new UnauthorizedAccessException("Configuration cannot flush unrelated tracked changes.");
        long revision = observed.Revision;
        if (enrollment is not null)
            revision = await PostgresStorageConfigurationContract.WriteAsync(context, enrollment, mutation, token);
        else
        {
            // Reject a substituted tracked value even if EF currently marks it Unchanged.
            foreach (var entry in context.ChangeTracker.Entries<SystemSetting>())
                if (observed.System.TryGetValue(entry.Entity.SettingKey, out var row) && StorageConfigurationRow.From(entry.Entity) != row)
                    throw new UnauthorizedAccessException("The original tracked system source changed.");
            foreach (var entry in context.ChangeTracker.Entries<TenantSetting>())
                if (entry.Entity.TenantId == selected!.AmbientTenant
                    && observed.Tenant.TryGetValue(entry.Entity.SettingKey, out var row) && StorageConfigurationRow.From(entry.Entity) != row)
                    throw new UnauthorizedAccessException("The original tracked tenant source changed.");
            foreach (var effect in mutation.Effects)
            {
                RequireOwner();
                if (effect.TenantId is { } tenant)
                {
                    var row = await context.Set<TenantSetting>().SingleOrDefaultAsync(
                        item => item.TenantId == tenant && item.SettingKey == effect.Key, token);
                    if (row is null)
                    {
                        row = new()
                        {
                            Id = effect.Id,
                            TenantId = tenant,
                            Tenant = null!,
                            SettingKey = effect.Key,
                            Value = effect.Value,
                            CreatedAt = effect.CreatedAt,
                            CreatedBy = effect.CreatedBy
                        };
                        context.Add(row);
                    }
                    row.Value = effect.Value; row.UpdatedAt = effect.UpdatedAt; row.UpdatedBy = effect.UpdatedBy;
                }
                else
                {
                    var row = await context.SystemSettings.SingleOrDefaultAsync(item => item.SettingKey == effect.Key, token);
                    if (row is null)
                    {
                        row = new()
                        {
                            Id = effect.Id,
                            SettingKey = effect.Key,
                            Value = effect.Value,
                            CreatedAt = effect.CreatedAt,
                            CreatedBy = effect.CreatedBy
                        };
                        context.Add(row);
                    }
                    row.Value = effect.Value; row.ValueType = effect.ValueType!.Value; row.IsLocked = false;
                    row.AllowedValues = effect.AllowedValues; row.Description = effect.Description;
                    row.Category = effect.Category; row.DisplayOrder = effect.DisplayOrder!.Value;
                    row.UpdatedAt = effect.UpdatedAt; row.UpdatedBy = effect.UpdatedBy;
                }
            }
            await context.SaveChangesAsync(token);
        }
        var after = observed with
        {
            Revision = revision,
            System = mutation.TenantId is null ? observed.System.SetItems(mutation.Effects.Select(row =>
                KeyValuePair.Create<string, StorageConfigurationRow?>(row.Key, row))) : observed.System,
            Tenant = mutation.TenantId.HasValue ? observed.Tenant.SetItems(mutation.Effects.Select(row =>
                KeyValuePair.Create<string, StorageConfigurationRow?>(row.Key, row))) : observed.Tenant
        };
        async Task RequireFinal(CancellationToken ct)
        {
            RequireOwner();
            await mutation.Source.RequireSourcesAsync(ct);
            if (!after.Same(await ReadAsync(ct)))
                throw new UnauthorizedAccessException("Configuration effects, metadata, bindings or unpatched sources changed.");
            RequireOwner();
        }
        await RequireFinal(token);
        mutation.Source.RequireTime();
        applied = mutation;
        completion = RequireFinal;
    }

    public Task RequireAppliedAsync(StorageSettingsMutation mutation, CancellationToken token)
    {
        if (!ReferenceEquals(applied, mutation) || completion is null)
            throw new UnauthorizedAccessException("The private fixed configuration writer did not complete.");
        return completion(token);
    }

    private async Task<StorageConfigurationState> ReadAsync(CancellationToken token)
    {
        requireSelection!();
        if (enrollment is not null)
            return await PostgresStorageConfigurationContract.ReadAsync(context, enrollment, selected!.AmbientTenant, token);
        var keys = StorageConfigurationState.Keys.ToArray();
        var system = await context.SystemSettings.AsNoTracking().Where(row => keys.Contains(row.SettingKey)).ToListAsync(token);
        Guid ambient = selected!.AmbientTenant;
        var tenant = await context.Set<TenantSetting>().IgnoreAllFilters(TenantFilterBypassReasons.StorageConfigurationOriginalSnapshot).AsNoTracking()
            .Where(row => row.TenantId == ambient && keys.Contains(row.SettingKey)).ToListAsync(token);
        string[] secretKeys = [SecretDefinitionRegistry.Keys.Storage.AccessKeyId, SecretDefinitionRegistry.Keys.Storage.SecretAccessKey];
        var bindings = await context.Set<SecretBinding>().AsNoTracking().Where(row =>
            secretKeys.Contains(row.SettingKey) && row.Qualifier == ""
            && (row.SettingScopeId == 1 && row.ScopeId == null || row.SettingScopeId == 2 && row.ScopeId == ambient))
            .OrderBy(row => row.Id).ToListAsync(token);
        var properties = context.Model.FindEntityType(typeof(SecretBinding))!.GetProperties().OrderBy(property => property.Name).ToArray();
        var result = new StorageConfigurationState(
            keys.ToImmutableDictionary(key => key, key => system.SingleOrDefault(row => row.SettingKey == key) is { } row
                ? StorageConfigurationRow.From(row) : null, StringComparer.Ordinal),
            keys.ToImmutableDictionary(key => key, key => tenant.SingleOrDefault(row => row.SettingKey == key) is { } row
                ? StorageConfigurationRow.From(row) : null, StringComparer.Ordinal),
            JsonSerializer.Serialize(bindings.Select(row => properties.ToDictionary(
                property => property.Name, property => property.PropertyInfo!.GetValue(row)))), 0);
        result.RequireShape(ambient);
        requireSelection();
        return result;
    }
}

public static class StorageConfigurationWriterRegistration
{
    public static IServiceCollection AddStorageConfigurationWriter(this IServiceCollection services)
    {
        services.TryAddScoped<IStorageConfigurationWriter, StorageConfigurationWriter>();
        return services;
    }
}
