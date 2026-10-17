using System.Globalization;
using System.Text.Json.Nodes;
using Explore.Secrets.Database;
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

    internal Task WriteMutationAsync(JsonObject scope, JsonObject before, JsonArray changes,
        JsonObject after, CancellationToken token) => context.Database.IsNpgsql()
        ? PostgresConfigurationManifestBootstrapContract.WriteAsync(context, data, scope, migration, global,
            before, changes, after, token)
        : WriteSqliteAsync(changes, token);

    internal Task WriteEvidenceAsync(JsonObject scope, JsonObject before, JsonArray changes,
        JsonObject after, CancellationToken token) => context.Database.IsNpgsql()
        ? PostgresConfigurationManifestBootstrapContract.WriteEvidenceAsync(context, data, scope,
            migration, global, before, changes, after, token)
        : WriteSqliteAsync(changes, token);

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
