using System.Collections.Immutable;
using System.Data.Common;
using System.Text.Json;
using Explore.Application.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Fixed publication-policy persistence on the caller's sealed, tenant-null PostgreSQL
/// import owner. Every operation uses its existing transaction and the same setting
/// locks as ordinary coordinated mutations. Compilation and side effects remain in
/// the application boundary; this capability changes only the five fixed settings.
/// </summary>
public static partial class PostgresConfigurationImportPublicationPolicyContract
{
    public const string OwnerRole = "event_configuration_import_publication_owner";
    public const string MigratorRole = "event_configuration_import_publication_migrator";
    public const string GlobalRole = "event_configuration_import_publication_global";
    private const int TenantPageSize = 500;

    public static Task<PublicationPolicyMutationSnapshot> ReadTenantSnapshotAsync(
        ExploreDbContext context, Guid tenantId, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        return ReadSnapshotAsync(context, tenantId, ct);
    }

    public static Task<PublicationPolicyMutationSnapshot> ReadInstanceSnapshotAsync(
        ExploreDbContext context, CancellationToken ct = default) =>
        ReadSnapshotAsync(context, null, ct);

    private static async Task<PublicationPolicyMutationSnapshot> ReadSnapshotAsync(
        ExploreDbContext context, Guid? tenantId, CancellationToken ct)
    {
        ValidateOwner(context);
        var systems = ImmutableArray.CreateBuilder<PublicationPolicySystemValueSnapshot>();
        var tenants = ImmutableArray.CreateBuilder<PublicationPolicyTenantValueSnapshot>();
        Guid? cursor = null;
        bool firstPage = true;
        do
        {
            using JsonDocument page = await ExecuteAsync(context, "read", ct,
                Parameter("tenant", NpgsqlDbType.Uuid, tenantId),
                Parameter("after", NpgsqlDbType.Uuid, cursor),
                Parameter("limit", NpgsqlDbType.Integer, TenantPageSize));
            JsonElement data = page.RootElement;
            if (firstPage)
            {
                foreach (JsonElement row in data.GetProperty("systems").EnumerateArray())
                    systems.Add(new PublicationPolicySystemValueSnapshot(
                        row.GetProperty("key").GetString()!,
                        row.GetProperty("value").GetString(),
                        row.GetProperty("locked").GetBoolean()));
                firstPage = false;
            }
            foreach (JsonElement row in data.GetProperty("tenants").EnumerateArray())
                tenants.Add(new PublicationPolicyTenantValueSnapshot(
                    row.GetProperty("tenant").GetGuid(),
                    row.GetProperty("key").GetString()!,
                    row.GetProperty("value").GetString()));
            JsonElement next = data.GetProperty("next");
            cursor = next.ValueKind == JsonValueKind.Null ? null : next.GetGuid();
        } while (cursor is not null);

        return new PublicationPolicyMutationSnapshot(
            systems.OrderBy(row => KeyOrder(row.Key)).ToImmutableArray(),
            tenants.OrderBy(row => row.TenantId).ThenBy(row => KeyOrder(row.Key)).ToImmutableArray());
    }

    public static Task<CoordinatedSettingMutationWriteResult> WriteTenantAsync(
        ExploreDbContext context, Guid tenantId,
        ImmutableArray<PublicationPolicySettingMutation> mutations,
        Guid? actor, DateTime utc, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        return WriteAsync(context, tenantId, mutations, actor, utc, ct);
    }

    public static Task<CoordinatedSettingMutationWriteResult> WriteInstanceAsync(
        ExploreDbContext context, ImmutableArray<PublicationPolicySettingMutation> mutations,
        Guid actor, DateTime utc, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(actor, Guid.Empty);
        return WriteAsync(context, null, mutations, actor, utc, ct);
    }

    private static async Task<CoordinatedSettingMutationWriteResult> WriteAsync(
        ExploreDbContext context, Guid? tenantId,
        ImmutableArray<PublicationPolicySettingMutation> mutations,
        Guid? actor, DateTime utc, CancellationToken ct)
    {
        ValidateOwner(context);
        if (utc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("UTC timestamp required.", nameof(utc));
        if (actor == Guid.Empty)
            throw new ArgumentException("An actor cannot be an empty UUID.", nameof(actor));
        if (mutations.IsDefault)
            throw new ArgumentException("The mutation batch must be initialized.", nameof(mutations));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (PublicationPolicySettingMutation? mutation in mutations)
        {
            if (mutation is null || mutation.Key is null
                || !PublicationPolicySettingKeys.All.Contains(mutation.Key, StringComparer.Ordinal)
                || !keys.Add(mutation.Key) || mutation.TenantId != tenantId)
                throw new ArgumentException("Invalid fixed-key mutation scope or duplicate key.", nameof(mutations));
            bool valid = mutation.Kind switch
            {
                PublicationPolicyMutationKind.Set => IsBoolean(mutation.JsonValue)
                    && (tenantId is null ? mutation.IsLocked is not null : mutation.IsLocked is null),
                PublicationPolicyMutationKind.Remove => mutation.JsonValue is null && mutation.IsLocked is null,
                _ => false
            };
            if (!valid)
                throw new ArgumentException("Invalid publication-policy mutation shape.", nameof(mutations));
        }
        string payload = JsonSerializer.Serialize(mutations.OrderBy(mutation => KeyOrder(mutation.Key))
            .Select(mutation => new
            {
                key = mutation.Key,
                kind = mutation.Kind == PublicationPolicyMutationKind.Set ? "set" : "remove",
                value = mutation.JsonValue,
                tenant = mutation.TenantId,
                locked = mutation.IsLocked,
                id = tenantId is not null && mutation.Kind == PublicationPolicyMutationKind.Set
                    ? (Guid?)Guid.CreateVersion7() : null
            }));
        using JsonDocument result = await ExecuteAsync(context, "write", ct,
            Parameter("tenant", NpgsqlDbType.Uuid, tenantId),
            Parameter("mutations", NpgsqlDbType.Jsonb, payload),
            Parameter("actor", NpgsqlDbType.Uuid, actor),
            Parameter("at", NpgsqlDbType.TimestampTz, utc));
        return new CoordinatedSettingMutationWriteResult(result.RootElement.EnumerateArray()
            .Select(row => new CoordinatedSettingValueChange(
                row.GetProperty("key").GetString()!,
                row.GetProperty("old").GetString(),
                row.GetProperty("new").GetString()))
            .ToImmutableArray());
    }

    private static bool IsBoolean(string? value)
    {
        if (value is null)
            return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind is JsonValueKind.True or JsonValueKind.False;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int KeyOrder(string key)
    {
        for (int index = 0; index < PublicationPolicySettingKeys.All.Count; index++)
            if (string.Equals(PublicationPolicySettingKeys.All[index], key, StringComparison.Ordinal))
                return index;
        throw new ArgumentException("Unknown publication-policy key.", nameof(key));
    }

    private static NpgsqlParameter Parameter(string name, NpgsqlDbType type, object? value) =>
        new(name, type) { Value = value ?? DBNull.Value };

    private static void ValidateOwner(ExploreDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ValidateTenantLease();
        if (!context.Database.IsNpgsql() || !context.IsConfigurationInstanceImportOwner
            || context.TenantFilterTenantId is not null || context.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Publication policy requires the sealed tenant-null PostgreSQL import owner and its active transaction.");
    }

    private static async Task<JsonDocument> ExecuteAsync(
        ExploreDbContext context, string operation, CancellationToken ct, params NpgsqlParameter[] parameters)
    {
        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"SELECT {FunctionSql(context, operation)}(" +
            string.Join(", ", parameters.Select(parameter => "@" + parameter.ParameterName)) + ")";
        foreach (NpgsqlParameter parameter in parameters)
            command.Parameters.Add(parameter);
        object? result = await command.ExecuteScalarAsync(ct);
        return JsonDocument.Parse((string)result!);
    }
}
