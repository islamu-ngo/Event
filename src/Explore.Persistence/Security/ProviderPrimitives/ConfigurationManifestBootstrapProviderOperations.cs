using System.Globalization;
using System.Text.Json.Nodes;
using Explore.Domain;
using Explore.Persistence.Services;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

namespace Explore.Persistence.Security;

/// <summary>Provider transport for closed bootstrap images and canonical row transitions.</summary>
internal sealed class ConfigurationManifestBootstrapProviderOperations
{
    private readonly ExploreDbContext context;
    private readonly ConfigurationManifestBootstrapData data;
    private readonly string migration;
    private readonly string global;

    internal ConfigurationManifestBootstrapProviderOperations(ExploreDbContext context,
        ConfigurationManifestBootstrapData data, IConfiguration configuration)
    {
        this.context = context;
        this.data = data;
        migration = context.Database.IsNpgsql() ? PrimaryDatabaseConfiguration.BindUsername(configuration, PrimaryDatabaseRole.Migrator) : "";
        global = context.Database.IsNpgsql() ? PrimaryDatabaseConfiguration.BindUsername(configuration, PrimaryDatabaseRole.Global) : "";
    }

    internal static void RequireSupported(ExploreDbContext context)
    {
        if (!context.Database.IsNpgsql() && !context.Database.IsSqlite())
            throw new NotSupportedException("configuration_manifest_bootstrap_provider_not_supported");
    }

    internal static DateTime Instant(ExploreDbContext context, DateTime value) => context.Database.IsNpgsql()
        ? new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc) : value;

    internal Task AttestAsync(CancellationToken token) => context.Database.IsNpgsql()
        ? PostgresConfigurationManifestBootstrapContract.AttestAsync(context, data, migration, global, token) : Task.CompletedTask;

    internal Task<JsonObject> ReadValidationAsync(JsonObject scope, CancellationToken token) => context.Database.IsNpgsql()
        ? PostgresConfigurationManifestBootstrapContract.ReadValidationAsync(context, data, scope, migration, global, token)
        : data.ReadSqliteAsync(scope, token);

    internal Task<JsonObject> ReadAsync(JsonObject scope, CancellationToken token) => context.Database.IsNpgsql()
        ? PostgresConfigurationManifestBootstrapContract.ReadAsync(context, data, scope, migration, global, token)
        : data.ReadSqliteAsync(scope, token);

    internal async Task WriteMutationAsync(JsonObject scope, JsonObject before, JsonArray changes,
        JsonObject after, CancellationToken token)
    {
        RequireRevisionPairs(scope, changes);
        if (context.Database.IsNpgsql())
            await PostgresConfigurationManifestBootstrapContract.WriteAsync(context, data, scope, migration, global,
                before, changes, after, token);
        else
            await WriteSqliteAsync(changes, token);
        foreach (var change in changes.OfType<JsonObject>()
                     .Where(change => change["Tag"]?.GetValue<string>() == "Systems"))
        {
            var row = change["After"]?.AsObject();
            string key = (row ?? change["Before"]!.AsObject())[nameof(SystemSetting.SettingKey)]!.GetValue<string>();
            await ManagedTenantProvisioningPolicyProjection.RecordAsync(context, key, row is null ? null : new SystemSetting
            {
                SettingKey = key,
                Value = row[nameof(SystemSetting.Value)]!.GetValue<string>(),
                IsLocked = row[nameof(SystemSetting.IsLocked)]!.GetValue<bool>()
            }, token);
        }
    }

    internal Task WriteEvidenceAsync(JsonObject scope, JsonObject before, JsonArray changes,
        JsonObject after, CancellationToken token)
    {
        RequireRevisionPairs(scope, changes);
        return context.Database.IsNpgsql()
            ? PostgresConfigurationManifestBootstrapContract.WriteEvidenceAsync(context, data, scope,
                migration, global, before, changes, after, token)
            : WriteSqliteAsync(changes, token);
    }

    private static void RequireRevisionPairs(JsonObject scope, JsonArray changes)
    {
        const string invalid = "Bootstrap revisions require exactly the original canonical tenant insertion effects.";
        if (changes.Any(change => change is not JsonObject effect || effect.Count != 3
                || !effect.ContainsKey("Tag") || !effect.ContainsKey("Before") || !effect.ContainsKey("After")))
            throw new UnauthorizedAccessException(invalid);
        var tenants = changes.OfType<JsonObject>().Where(change => change["Tag"]?.GetValue<string>() == "Tenants").ToArray();
        var revisions = changes.OfType<JsonObject>()
            .Where(change => change["Tag"]?.GetValue<string>() == ConfigurationManifestBootstrapData.RevisionTag).ToArray();
        if (tenants.Length != revisions.Length)
            throw new UnauthorizedAccessException(invalid);
        if (tenants.Length == 0)
            return;
        var insertedTenants = new HashSet<Guid>();
        foreach (var tenant in tenants)
        {
            if (tenant["Before"] is not null || tenant["After"] is not JsonObject row
                || !Guid.TryParse(row["Id"]?.ToString(), out Guid id) || !insertedTenants.Add(id))
                throw new UnauthorizedAccessException(invalid);
        }
        var reserved = scope[ConfigurationManifestBootstrapData.RevisionTag]!.AsArray()
            .Select(value => value!.GetValue<Guid>()).ToHashSet();
        var ids = new HashSet<Guid>();
        var pairedTenants = new HashSet<Guid>();
        foreach (var revision in revisions)
        {
            if (revision["Before"] is not null || revision["After"] is not JsonObject row || row.Count != 4
                || !Guid.TryParse(row["Id"]?.ToString(), out Guid id) || id.Version != 7
                || !reserved.Contains(id) || !ids.Add(id)
                || !Guid.TryParse(row["TenantId"]?.ToString(), out Guid tenantId)
                || !insertedTenants.Contains(tenantId) || !pairedTenants.Add(tenantId)
                || row["IdentityEpoch"] is not JsonValue identity || !identity.TryGetValue<long>(out long identityEpoch) || identityEpoch != 0
                || row["DisclosureEpoch"] is not JsonValue disclosure || !disclosure.TryGetValue<long>(out long disclosureEpoch) || disclosureEpoch != 1)
                throw new UnauthorizedAccessException(invalid);
        }
    }

    private async Task WriteSqliteAsync(JsonArray changes, CancellationToken token)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in changes.OfType<JsonObject>())
        {
            string tag = change["Tag"]!.GetValue<string>();
            var relation = data.Relations.Single(relation => relation.Tag == tag);
            var after = change["After"]!.AsObject();
            var before = change["Before"]?.AsObject();
            if (!keys.Add(tag + "\n" + ConfigurationManifestBootstrapData.Key(relation, after)))
                throw new UnauthorizedAccessException("A bootstrap writer cannot duplicate an effect.");
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
            string Param(ConfigurationManifestBootstrapData.Column column, JsonNode? node, string name)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                Type type = column.Property.GetRelationalTypeMapping().Converter?.ProviderClrType ?? column.Property.ClrType;
                type = Nullable.GetUnderlyingType(type) ?? type;
                parameter.Value = node is null ? DBNull.Value
                    : column.Time ? new DateTime(node.GetValue<long>(), DateTimeKind.Utc)
                    : column.Json ? node.ToJsonString()
                    : type == typeof(Guid) ? node.GetValue<Guid>()
                    : type == typeof(string) ? node.GetValue<string>()
                    : type == typeof(bool) ? node.GetValue<bool>()
                    : Convert.ChangeType(node.ToJsonString(), type.IsEnum ? Enum.GetUnderlyingType(type) : type, CultureInfo.InvariantCulture);
                command.Parameters.Add(parameter);
                return "@" + name;
            }
            if (before is null)
                command.CommandText = $"INSERT INTO {relation.Table} ({string.Join(", ", relation.Columns.Select(column => column.Sql))}) " +
                    $"VALUES ({string.Join(", ", relation.Columns.Select((column, index) => Param(column, after[column.Name], "a" + index)))}) ON CONFLICT DO NOTHING";
            else
                command.CommandText = $"UPDATE {relation.Table} SET " +
                    string.Join(", ", relation.Columns.Select((column, index) => column.Sql + " = " + Param(column, after[column.Name], "a" + index))) +
                    " WHERE " + string.Join(" AND ", relation.Columns.Select((column, index) =>
                        column.Sql + " IS " + Param(column, before[column.Name], "b" + index)));
            if (await command.ExecuteNonQueryAsync(token) != 1)
                throw new UnauthorizedAccessException("The retained bootstrap row or absence changed.");
        }
    }
}
