using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Secrets;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Services;

/// <summary>
/// Reads the fixed storage binding surface on the probe's retained ordinary owner
/// without acquiring mutation authority or changing its connection lifetime.
/// </summary>
public sealed class StorageProbeSecretBindingsReader(
    ExploreDbContext context,
    IServiceProvider services,
    PostgresNativeQueueEnrollment? enrollment = null) : IStorageProbeSecretBindingsReader
{
    public async Task<(IReadOnlyList<SecretBinding> Bindings, string Metadata)> ReadAsync(
        Guid ambientTenant, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selection = services.GetRequiredService<ExploreDbContextExecutionLease>();
        selection.RequireOriginalOwner(context);
        StorageProbeSourceProviderSession.RequireSupported(context);
        var contextId = context.ContextId;
        var tenantContext = context.TenantContext;
        var selectedTenant = selection.SelectedTenantId;
        var humanScope = selection.HumanScope;
        var humanLifetime = selection.HumanLifetime;
        var connection = RelationalConnectionIdentity.Capture(context);
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        if (ambientTenant == Guid.Empty || humanScope?.Subject is null
            || connection.State != ConnectionState.Open || transaction is null)
            throw new UnauthorizedAccessException("Storage bindings require the original active probe source owner.");
        int? backend = connection.BackendProcessId;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change)
        {
            if (change.CurrentState != ConnectionState.Open) interrupted = true;
        }
        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            selection.RequireOriginalOwner(context);
            if (context.ContextId != contextId || !ReferenceEquals(context.TenantContext, tenantContext)
                || selection.SelectedTenantId != selectedTenant || context.TenantFilterTenantId != selectedTenant
                || selectedTenant.HasValue && selectedTenant.Value != ambientTenant
                || selection.PolicyPurpose != PolicyMutationPurpose.Standard
                || !ReferenceEquals(selection.HumanScope, humanScope)
                || !ReferenceEquals(selection.HumanLifetime, humanLifetime)
                || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
                || interrupted || !connection.Matches(context) || connection.State != ConnectionState.Open
                || connection.BackendProcessId != backend || !connection.Owns(transaction)
                || !ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), transaction)
                || transaction.IsolationLevel != IsolationLevel.Serializable
                || !context.HumanCommitAttemptOwnsTransaction
                || System.Transactions.Transaction.Current is not null
                || context.ChangeTracker.Entries().Any(entry =>
                    entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
                throw new UnauthorizedAccessException("The original probe binding source owner changed.");
        }

        RequireOriginal();
        connection.StateChange += Changed;
        try
        {
            string[] keys =
            [
                SecretDefinitionRegistry.Keys.Storage.AccessKeyId,
                SecretDefinitionRegistry.Keys.Storage.SecretAccessKey
            ];
            var properties = context.Model.FindEntityType(typeof(SecretBinding))!.GetProperties()
                .OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
            SecretBinding[] bindings;
            if (context.Database.IsNpgsql())
            {
                if (enrollment is null)
                    throw new UnauthorizedAccessException("Probe storage binding enrollment is required.");
                var state = await PostgresStorageConfigurationContract.ReadAsync(
                    context, enrollment, ambientTenant, cancellationToken);
                RequireOriginal();
                using var document = JsonDocument.Parse(state.Bindings);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    throw new UnauthorizedAccessException("The storage binding source shape is invalid.");
                var rows = new List<SecretBinding>();
                foreach (var row in document.RootElement.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object
                        || row.EnumerateObject().Count() != properties.Length
                        || !row.TryGetProperty(nameof(SecretBinding.SettingKey), out var key)
                        || key.ValueKind != JsonValueKind.String)
                        throw new UnauthorizedAccessException("The storage binding source shape is invalid.");
                    var binding = new SecretBinding { SettingKey = key.GetString()! };
                    foreach (var property in properties)
                    {
                        if (!row.TryGetProperty(property.Name, out var value)
                            || value.ValueKind == JsonValueKind.Null && !property.IsNullable)
                            throw new UnauthorizedAccessException("The storage binding metadata is incomplete.");
                        property.PropertyInfo!.SetValue(binding, value.Deserialize(property.ClrType));
                    }
                    rows.Add(binding);
                }
                bindings = rows.ToArray();
            }
            else
            {
                bindings = await context.Set<SecretBinding>().AsNoTracking()
                    .Where(row => keys.Contains(row.SettingKey) && row.Qualifier == ""
                        && (row.SettingScopeId == 1 && row.ScopeId == null
                            || row.SettingScopeId == 2 && row.ScopeId == ambientTenant))
                    .ToArrayAsync(cancellationToken);
                RequireOriginal();
            }

            var ids = new HashSet<Guid>();
            var identities = new HashSet<(string Key, int Scope, Guid? ScopeId)>();
            foreach (var binding in bindings)
            {
                if (!keys.Contains(binding.SettingKey, StringComparer.Ordinal) || binding.Qualifier != string.Empty
                    || !(binding.SettingScopeId == 1 && binding.ScopeId is null
                        || binding.SettingScopeId == 2 && binding.ScopeId == ambientTenant)
                    || SecretDefinitionRegistry.TryGet(binding.SettingKey) is not { } definition
                    || !definition.AllowedScopes.Contains(binding.Scope)
                    || binding.Id == Guid.Empty || !ids.Add(binding.Id)
                    || !identities.Add((binding.SettingKey, binding.SettingScopeId, binding.ScopeId)))
                    throw new UnauthorizedAccessException("The storage binding source is outside its unique bounded scope.");
            }
            bindings = bindings.OrderBy(binding => binding.Id).ToArray();
            string metadata = JsonSerializer.Serialize(bindings.Select(binding => properties.ToDictionary(
                property => property.Name, property => property.PropertyInfo!.GetValue(binding),
                StringComparer.Ordinal)));
            RequireOriginal();
            return (bindings, metadata);
        }
        finally
        {
            connection.StateChange -= Changed;
        }
    }
}
