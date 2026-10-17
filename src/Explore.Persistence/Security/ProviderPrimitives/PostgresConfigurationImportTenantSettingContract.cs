using System.Data;
using System.Data.Common;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Ordinary manifest tenant scalar upserts on the sealed Global import transaction.
/// Full original rows come from the import snapshot capability; no private EF DML,
/// additional connection, transaction, commit, or immediate cache effect is permitted.
/// </summary>
public static partial class PostgresConfigurationImportTenantSettingContract
{
    public const string OwnerRole = "event_configuration_import_tenant_setting_owner";
    public const string MigratorRole = "event_configuration_import_tenant_setting_migrator";
    public const string GlobalRole = "event_configuration_import_tenant_setting_global";
    private static readonly JsonSerializerOptions SnapshotJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task UpsertAsync(
        ExploreDbContext context, Guid tenantId,
        IReadOnlyCollection<TenantSettingOverrideUpsert> overrides, Guid actorId,
        CancellationToken ct = default)
    {
        DbTransaction transaction = ValidateOwner(context);
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(actorId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(overrides);
        ConfigurationManifestSettingCatalogEntry[] catalog = OrdinaryEntries();
        TenantSettingOverrideUpsert[] writes = overrides.OrderBy(row => row.SettingKey, StringComparer.Ordinal).ToArray();
        if (writes.Length > catalog.Length
            || writes.Select(row => row.SettingKey).Distinct(StringComparer.Ordinal).Count() != writes.Length)
            throw new ArgumentException("Tenant scalar import keys must be distinct catalog members.", nameof(overrides));
        foreach (TenantSettingOverrideUpsert write in writes)
        {
            ConfigurationManifestSettingCatalogEntry entry = catalog.SingleOrDefault(
                candidate => candidate.Definition.Key == write.SettingKey)
                ?? throw new ArgumentException("The tenant scalar import key is not admitted.", nameof(overrides));
            if (write.IsLocked)
                throw new ArgumentException("Tenant scalar imports cannot change locks.", nameof(overrides));
            ValidateValue(entry, write.Value);
        }
        if (writes.Length == 0)
            return;
        if (writes.Any(row => EventDiscoveryDisclosureMutationScope.IsDisclosureSetting(row.SettingKey)))
            context.DisclosureMutations.Enlist([tenantId]);

        Dictionary<string, TenantSetting> originals = (await PostgresConfigurationImportSnapshotContract
            .ReadTenantSettingsAsync(context, tenantId, writes.Select(row => row.SettingKey).ToArray(), ct))
            .ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        var batch = writes.Select(write =>
        {
            originals.TryGetValue(write.SettingKey, out TenantSetting? original);
            return new Mutation(write.SettingKey, write.Value, original?.Id ?? Guid.CreateVersion7(),
                original is null ? null : new RowSnapshot(original.Id, original.TenantId,
                    original.SettingKey, original.Value, original.IsLocked,
                    Instant(original.CreatedAt), original.CreatedBy,
                    original.UpdatedAt is { } updated ? Instant(updated) : null, original.UpdatedBy));
        }).ToArray();
        DbConnection connection = context.Database.GetDbConnection();
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {FunctionSql(context, "upsert")}(@tenant, @writes, @actor, @at)";
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = tenantId });
        command.Parameters.Add(new NpgsqlParameter("writes", NpgsqlDbType.Jsonb)
        { Value = JsonSerializer.Serialize(batch, SnapshotJson) });
        command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Uuid) { Value = actorId });
        command.Parameters.Add(new NpgsqlParameter("at", NpgsqlDbType.TimestampTz) { Value = Instant(DateTime.UtcNow) });
        if (context.Database.GetCommandTimeout() is { } timeout)
            command.CommandTimeout = timeout;
        object? result = await command.ExecuteScalarAsync(ct);
        if (!ReferenceEquals(transaction, ValidateOwner(context))
            || !ReferenceEquals(connection, context.Database.GetDbConnection()))
            throw new InvalidOperationException("Tenant scalar import must retain its original connection and transaction.");
        using JsonDocument accepted = JsonDocument.Parse((string)result!);
        if (!accepted.RootElement.GetBoolean())
            throw new DbUpdateConcurrencyException("Tenant settings changed since the import snapshot was read.");
    }

    private static DateTime Instant(DateTime utc)
    {
        if (utc == default || utc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A nondefault UTC timestamp is required.", nameof(utc));
        return new DateTime(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);
    }

    private static DbTransaction ValidateOwner(ExploreDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ValidateTenantLease();
        if (!context.Database.IsNpgsql() || !context.IsConfigurationInstanceImportOwner
            || context.TenantFilterTenantId is not null || context.Database.CurrentTransaction is null
            || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new InvalidOperationException("Tenant scalar import requires the sealed tenant-null Global owner.");
        DbTransaction transaction = context.Database.CurrentTransaction.GetDbTransaction();
        if (!ReferenceEquals(transaction.Connection, context.Database.GetDbConnection())
            || transaction.IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Tenant scalar import requires its serializable owner connection.");
        return transaction;
    }

    private static ConfigurationManifestSettingCatalogEntry[] OrdinaryEntries() =>
        ConfigurationManifestCatalog.TenantSettings.Values
            .Where(entry => entry.Scope == ConfigurationManifestScope.Tenant
                && ReferenceEquals(entry.Definition, SettingRegistry.Get(entry.Definition.Key))
                && !entry.Definition.RequiresCoordinatedMutation && !entry.Definition.IsSensitive
                && entry.Definition.MinScope <= SettingScope.Tenant && entry.Definition.MaxScope >= SettingScope.Tenant
                && !PublicationPolicySettingKeys.All.Contains(entry.Definition.Key, StringComparer.Ordinal)
                && !EventResourceSettingMutationGuard.Handles(entry.Definition.Key)
                && entry.Definition.Key != EventResourceProviderBindingDocument.SettingKey
                && !VisitorAccessSettingMutationGuard.Handles(entry.Definition.Key)
                && !EmailDeliverySettingKeys.Contains(entry.Definition.Key)
                && !RelationalSettingMutationLock.RequiresEmailDeliveryFence([entry.Definition.Key])
                && !InstanceOperatorIdentitySettingKeys.Contains(entry.Definition.Key)
                && entry.Definition.Key is not (GovernanceSettingKeys.LocationPrivacy.AllowHomeLocations
                    or GovernanceSettingKeys.LocationPrivacy.AllowPublicExactAddress
                    or GovernanceSettingKeys.LocationPrivacy.AllowPublicCoordinates
                    or GovernanceSettingKeys.LocationPrivacy.MinimumHomeAudience
                    or GovernanceSettingKeys.LocationPrivacy.DefaultRevealOffset))
            .OrderBy(entry => entry.Definition.Key, StringComparer.Ordinal).ToArray();

    private static void ValidateValue(ConfigurationManifestSettingCatalogEntry entry, string value)
    {
        using JsonDocument document = JsonDocument.Parse(value);
        JsonElement scalar = document.RootElement;
        bool valid = entry.Definition.ValueType switch
        {
            SettingValueType.Boolean => scalar.ValueKind is JsonValueKind.True or JsonValueKind.False,
            SettingValueType.String => scalar.ValueKind == JsonValueKind.String
                && (entry.MaximumStringLength is not { } maximum || scalar.GetString()!.Length <= maximum)
                && (entry.Definition.AllowedValues is null
                    || entry.Definition.AllowedValues.Contains(scalar.GetString()!, StringComparer.Ordinal)),
            _ => false
        };
        if (!valid)
            throw new ArgumentException("Tenant scalar import value does not match its catalog definition.", nameof(value));
    }

    private sealed record Mutation(string Key, string Value, Guid Id, RowSnapshot? Original);
    private sealed record RowSnapshot(Guid Id, Guid Tenant, string Key, string Value, bool Locked,
        DateTime Created, Guid? Creator, DateTime? Updated, Guid? Updater);
}
