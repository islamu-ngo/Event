using System.Data;
using System.Data.Common;
using System.Text.Json;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Domain;
using Explore.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Fixed resource-setting snapshots and create/CAS writes on the sealed Global import
/// owner's existing serializable transaction. The writer retains Domain policy validation
/// and deferred effects; this capability never tracks entities or commits the import.
/// API authentication supplies the original human at the managed boundary. The database
/// service login is trusted; the SQL capability does not authenticate an API credential.
/// </summary>
public static partial class PostgresConfigurationImportEventResourceContract
{
    public const string OwnerRole = "event_configuration_import_resource_owner";
    public const string MigratorRole = "event_configuration_import_resource_migrator";
    public const string GlobalRole = "event_configuration_import_resource_global";
    private static readonly JsonSerializerOptions SnapshotJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    internal static void ValidateMutations(
        IEnumerable<EventResourceSettingMutation> mutations, Guid? actor)
    {
        if (actor is null || actor == Guid.Empty)
            throw new ArgumentException("Resource import requires an audit actor.", nameof(actor));
        foreach (EventResourceSettingMutation mutation in mutations)
        {
            if (mutation.Kind != EventResourceSettingMutationKind.SetValue || mutation.IsLocked is not null)
                throw new ArgumentException("Resource import admits only value mutations without lock changes.", nameof(mutations));
            ValidateCatalog(mutation.Key, mutation.TenantId);
        }
    }

    private static SettingDefinition ValidateCatalog(string key, Guid? tenant)
    {
        var catalog = tenant is null
            ? ConfigurationManifestCatalog.InstanceSettings : ConfigurationManifestCatalog.TenantSettings;
        if (!EventResourceSettingMutationGuard.Handles(key)
            || tenant == Guid.Empty || !catalog.TryGetValue(key, out var entry)
            || !ReferenceEquals(entry.Definition, SettingRegistry.Get(key))
            || entry.Scope != (tenant is null ? ConfigurationManifestScope.Instance : ConfigurationManifestScope.Tenant)
            || entry.Definition.IsSensitive || !entry.Definition.RequiresCoordinatedMutation)
            throw new ArgumentException("Resource import key does not match the fixed manifest registry.", nameof(key));
        return entry.Definition;
    }

    internal static async Task<Dictionary<string, SystemSetting>> ReadSystemsAsync(
        ExploreDbContext context, CancellationToken ct)
    {
        using JsonDocument result = await ExecuteAsync(context, "read", ct);
        return result.RootElement.EnumerateArray().Select(row => new SystemSetting
        {
            Id = row.GetProperty("id").GetGuid(),
            SettingKey = row.GetProperty("key").GetString()!,
            Value = row.GetProperty("value").GetString()!,
            SettingValueTypeId = row.GetProperty("type").GetInt32(),
            IsLocked = row.GetProperty("locked").GetBoolean(),
            AllowedValues = row.GetProperty("allowed").GetString(),
            Description = row.GetProperty("description").GetString(),
            Category = row.GetProperty("category").GetString(),
            DisplayOrder = row.GetProperty("order").GetInt32(),
            CreatedAt = row.GetProperty("created").GetDateTime().ToUniversalTime(),
            CreatedBy = NullableGuid(row, "creator"),
            UpdatedAt = row.GetProperty("updated").ValueKind == JsonValueKind.Null
                ? null : row.GetProperty("updated").GetDateTime().ToUniversalTime(),
            UpdatedBy = NullableGuid(row, "updater")
        }).ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
    }

    internal static async Task<Dictionary<(Guid TenantId, string Key), TenantSetting>> ReadTenantsAsync(
        ExploreDbContext context, Guid[] tenantIds, CancellationToken ct)
    {
        ValidateOwner(context);
        var result = new Dictionary<(Guid TenantId, string Key), TenantSetting>();
        string[] keys = EventResourceSettingMutationGuard.Keys
            .Where(key => SettingRegistry.Get(key)!.MaxScope >= SettingScope.Tenant).ToArray();
        foreach (Guid tenant in tenantIds)
        {
            foreach (TenantSetting row in await PostgresConfigurationImportSnapshotContract
                         .ReadTenantSettingsAsync(context, tenant, keys, ct))
                result.Add((row.TenantId, row.SettingKey), row);
        }
        return result;
    }

    // Capture before the writer mutates the restored entity. Both JSON snapshots and
    // binary timestamp parameters use PostgreSQL's microsecond precision.
    internal static string Capture(object entity) => JsonSerializer.Serialize(entity switch
    {
        SystemSetting row => new RowSnapshot(row.Id, null, row.SettingKey, row.Value, row.IsLocked,
            row.SettingValueTypeId, row.AllowedValues, row.Description, row.Category, row.DisplayOrder,
            Instant(row.CreatedAt), row.CreatedBy, NullableInstant(row.UpdatedAt), row.UpdatedBy),
        TenantSetting row => new RowSnapshot(row.Id, row.TenantId, row.SettingKey, row.Value, row.IsLocked,
            null, null, null, null, null,
            Instant(row.CreatedAt), row.CreatedBy, NullableInstant(row.UpdatedAt), row.UpdatedBy),
        _ => throw new ArgumentException("A resource setting entity is required.", nameof(entity))
    }, SnapshotJson);

    internal static DateTime Instant(DateTime utc)
    {
        if (utc == default || utc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A nondefault UTC timestamp is required.", nameof(utc));
        return new DateTime(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);
    }

    private static DateTime? NullableInstant(DateTime? utc) => utc is { } value ? Instant(value) : null;

    internal static async Task WriteAsync(
        ExploreDbContext context, object entity, EntityState state, string? original,
        HumanCommitSubject subject, IInstanceAdministratorCommitAuthority authority,
        Guid? actor, DateTime utc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(authority);
        DbTransaction owner = ValidateOwner(context);
        if (!await authority.IsInstanceAdministratorForCommitAsync(subject.PrincipalId, ct))
            throw new UnauthorizedAccessException("The original import human must retain instance administration.");
        if (!ReferenceEquals(owner, ValidateOwner(context)))
            throw new InvalidOperationException("Resource admission must retain its original transaction.");
        if (state is not (EntityState.Added or EntityState.Modified)
            || (state == EntityState.Added) != (original is null))
            throw new ArgumentException("Resource import admits only create/CAS value writes.", nameof(state));
        RowSnapshot current = JsonSerializer.Deserialize<RowSnapshot>(Capture(entity), SnapshotJson)!;
        SettingDefinition definition = ValidateCatalog(current.Key, current.Tenant);
        if (actor is null || actor == Guid.Empty || current.Updated != Instant(utc) || current.Updater != actor
            || current.Id == Guid.Empty)
            throw new ArgumentException("Resource import audit metadata does not match the writer.", nameof(entity));
        if (state == EntityState.Added && (current.Locked || current.Creator != actor || current.Created != current.Updated
            || current.Tenant is null && (current.Type != (int)definition.ValueType
                || current.Allowed is not null || current.Description != definition.Description
                || current.Category != definition.Category || current.Order != 0)))
            throw new ArgumentException("Resource creation metadata must match the registry.", nameof(entity));

        using JsonDocument result = await ExecuteAsync(context, "write", ct,
            Parameter("current", NpgsqlDbType.Jsonb, Capture(entity)),
            Parameter("original", NpgsqlDbType.Jsonb, original),
            Parameter("actor", NpgsqlDbType.Uuid, actor),
            Parameter("at", NpgsqlDbType.TimestampTz, Instant(utc)));
        if (!result.RootElement.GetBoolean())
            throw new DbUpdateConcurrencyException("Resource settings changed since the import snapshot was read.");
    }

    private static Guid? NullableGuid(JsonElement row, string name) =>
        row.GetProperty(name).ValueKind == JsonValueKind.Null ? null : row.GetProperty(name).GetGuid();

    private static NpgsqlParameter Parameter(string name, NpgsqlDbType type, object? value) =>
        new(name, type) { Value = value ?? DBNull.Value };

    private static DbTransaction ValidateOwner(ExploreDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ValidateTenantLease();
        if (!context.Database.IsNpgsql() || !context.IsConfigurationInstanceImportOwner
            || context.TenantFilterTenantId is not null || context.Database.CurrentTransaction is null
            || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new InvalidOperationException(
                "Resource import requires the sealed tenant-null Global owner and its active transaction.");
        DbTransaction transaction = context.Database.CurrentTransaction.GetDbTransaction();
        if (!ReferenceEquals(transaction.Connection, context.Database.GetDbConnection())
            || transaction.IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Resource import requires its serializable owner connection.");
        return transaction;
    }

    private static async Task<JsonDocument> ExecuteAsync(
        ExploreDbContext context, string operation, CancellationToken ct, params NpgsqlParameter[] parameters)
    {
        DbTransaction transaction = ValidateOwner(context);
        DbConnection connection = context.Database.GetDbConnection();
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {FunctionSql(context, operation)}(" +
            string.Join(", ", parameters.Select(parameter => "@" + parameter.ParameterName)) + ")";
        foreach (NpgsqlParameter parameter in parameters)
            command.Parameters.Add(parameter);
        if (context.Database.GetCommandTimeout() is { } timeout)
            command.CommandTimeout = timeout;
        object? result = await command.ExecuteScalarAsync(ct);
        if (!ReferenceEquals(transaction, ValidateOwner(context))
            || !ReferenceEquals(connection, context.Database.GetDbConnection()))
            throw new InvalidOperationException("Resource import must retain its original connection and transaction.");
        return JsonDocument.Parse((string)result!);
    }

    private sealed record RowSnapshot(
        Guid Id, Guid? Tenant, string Key, string Value, bool Locked,
        int? Type, string? Allowed, string? Description, string? Category, int? Order,
        DateTime Created, Guid? Creator, DateTime? Updated, Guid? Updater);
}
