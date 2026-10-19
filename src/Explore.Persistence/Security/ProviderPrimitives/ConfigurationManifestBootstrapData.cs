using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Closed model-derived row vocabulary for the original nonhuman manifest writer.</summary>
internal sealed class ConfigurationManifestBootstrapData(ExploreDbContext context)
{
    internal static readonly (string Tag, Type Type)[] Types =
    [
        ("Systems", typeof(SystemSetting)), ("Tenants", typeof(Tenant)), ("Overrides", typeof(TenantSetting)),
        ("Documents", typeof(TenantSettingsDocument)), ("Policies", typeof(PaidEventPolicyVersion)),
        ("Organizers", typeof(PaidEventPolicyAllowedOrganizerKind)), ("Currencies", typeof(PaidEventPolicyAllowedCurrency)),
        ("Refunds", typeof(PaidEventPolicyRefundProtection)), ("Limits", typeof(PaidEventPolicyCurrencyRiskLimitRow)),
        ("Operations", typeof(ConfigurationManifestOperation)), ("Results", typeof(ConfigurationManifestTenantResult)),
        ("Outbox", typeof(OutboxMessage))
    ];

    internal sealed record Column(string Name, string Sql, string StoreType, bool Time, bool Json, IProperty Property);
    internal sealed record Relation(string Tag, Type Type, string Table, string Schema,
        ImmutableArray<Column> Columns, ImmutableArray<Column> Keys)
    {
        internal string Field(string name) => Columns.Single(column => column.Name == name).Sql;
    }

    internal ImmutableArray<Relation> Relations => [.. Types.Select(pair =>
    {
        var entity = context.Model.FindEntityType(pair.Type)!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var sql = context.GetService<ISqlGenerationHelper>();
        var columns = entity.GetProperties().OrderBy(property => property.Name, StringComparer.Ordinal).Select(property =>
        {
            Type type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            string storeType = property.GetColumnType() ?? property.GetRelationalTypeMapping().StoreType;
            return new Column(property.Name, sql.DelimitIdentifier(property.GetColumnName(store)!),
                storeType, type == typeof(DateTime) || type == typeof(DateTimeOffset),
                storeType is "jsonb" or "json", property);
        }).ToImmutableArray();
        return new Relation(pair.Tag, pair.Type, sql.DelimitIdentifier(entity.GetTableName()!,
                context.Database.IsNpgsql() ? entity.GetSchema() ?? "public" : entity.GetSchema()),
            entity.GetSchema() ?? "public", columns,
            entity.FindPrimaryKey()!.Properties.Select(property => columns.Single(column => column.Property == property)).ToImmutableArray());
    })];

    internal JsonObject Row(object entity)
    {
        var relation = Relations.Single(relation => relation.Type == entity.GetType());
        var entry = context.Entry(entity);
        var result = new JsonObject();
        foreach (var column in relation.Columns)
            result[column.Name] = ProviderValue(column, entry.Property(column.Name).CurrentValue);
        return result;
    }

    internal JsonNode? ProviderValue(string tag, string property, object? value) =>
        ProviderValue(Relations.Single(relation => relation.Tag == tag).Columns.Single(column => column.Name == property), value);

    private object? ProviderScalar(string tag, string property, object? value) =>
        Relations.Single(relation => relation.Tag == tag).Columns.Single(column => column.Name == property)
            .Property.GetRelationalTypeMapping().Converter?.ConvertToProvider(value) ?? value;

    private static JsonNode? ProviderValue(Column column, object? value)
    {
        var converter = column.Property.GetRelationalTypeMapping().Converter;
        if (converter is not null) value = converter.ConvertToProvider(value);
        return value switch
        {
            null => null,
            DateTime instant => JsonValue.Create(instant.Ticks),
            DateTimeOffset instant => JsonValue.Create(instant.UtcTicks),
            string text when column.Json => JsonNode.Parse(text),
            _ => JsonSerializer.SerializeToNode(value)
        };
    }

    internal IEnumerable<object> PolicyRows(PaidEventPolicyVersion policy)
    {
        yield return policy;
        foreach (var navigation in context.Entry(policy).Collections)
            if (navigation.CurrentValue is IEnumerable values)
                foreach (object value in values)
                    if (Types.Any(pair => pair.Type == value.GetType())) yield return value;
    }

    internal static string[] SystemReadKeys =>
        [.. ConfigurationManifestCatalog.InstanceSettings.Keys.Concat(ConfigurationManifestCatalog.TenantSettings.Keys)
            .Concat(TenantBrandingGovernanceMutationLockKeys.All).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    internal static string[] CrossTenantKeys =>
        [.. PublicationPolicySettingKeys.All.Concat(EventResourceSettingMutationGuard.Keys).Distinct(StringComparer.Ordinal)];

    internal static string Key(Relation relation, JsonObject row) =>
        string.Join("\u001f", relation.Keys.Select(column => row[column.Name]?.ToJsonString() ?? "null"));

    internal static JsonArray Rows(JsonObject image, string tag) => image[tag]!.AsArray();

    internal static bool Same(JsonObject left, JsonObject right) => JsonNode.DeepEquals(left, right);

    internal void Order(JsonObject image)
    {
        foreach (var relation in Relations)
            image[relation.Tag] = new JsonArray(Rows(image, relation.Tag).OfType<JsonObject>()
                .Order(Comparer<JsonObject>.Create((left, right) =>
                {
                    foreach (var key in relation.Keys)
                    {
                        var type = Nullable.GetUnderlyingType(key.Property.ClrType) ?? key.Property.ClrType;
                        int comparison = type == typeof(Guid)
                            ? left[key.Name]!.GetValue<Guid>().CompareTo(right[key.Name]!.GetValue<Guid>())
                            : type == typeof(string)
                                ? StringComparer.Ordinal.Compare(left[key.Name]!.GetValue<string>(), right[key.Name]!.GetValue<string>())
                                : decimal.Parse(left[key.Name]!.ToJsonString(), CultureInfo.InvariantCulture)
                                    .CompareTo(decimal.Parse(right[key.Name]!.ToJsonString(), CultureInfo.InvariantCulture));
                        if (comparison != 0) return comparison;
                    }
                    return 0;
                })).Select(row => row.DeepClone()).ToArray());
    }

    internal static void RetainObservedIdentities(JsonObject scope, JsonObject image)
    {
        foreach (string tag in new[] { "Tenants", "Documents", "Policies", "Operations" })
        {
            var retained = scope[tag]!.AsArray().Select(value => value!.GetValue<Guid>()).ToHashSet();
            foreach (var row in Rows(image, tag))
                retained.Add(row!["Id"]!.GetValue<Guid>());
            scope[tag] = new JsonArray(retained.Order().Select(id => JsonValue.Create(id)).Cast<JsonNode?>().ToArray());
        }
    }

    internal async Task<JsonObject> ReadSqliteAsync(JsonObject scope, CancellationToken token)
    {
        var result = new JsonObject();
        foreach (var relation in Relations)
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            string table = relation.Table;
            var predicates = SqlitePredicate(relation, scope, command);
            command.CommandText = $"SELECT {string.Join(", ", relation.Columns.Select(column => column.Sql))} FROM {table} WHERE {predicates} " +
                $"ORDER BY {string.Join(", ", relation.Keys.Select(column => column.Sql))}";
            var rows = new JsonArray();
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var row = new JsonObject();
                for (int index = 0; index < relation.Columns.Length; index++)
                {
                    var column = relation.Columns[index];
                    if (reader.IsDBNull(index)) { row[column.Name] = null; continue; }
                    Type type = column.Property.GetRelationalTypeMapping().Converter?.ProviderClrType ?? column.Property.ClrType;
                    type = Nullable.GetUnderlyingType(type) ?? type;
                    object value = column.Time ? reader.GetDateTime(index).Ticks
                        : type == typeof(Guid) ? reader.GetGuid(index)
                        : type == typeof(byte[]) ? (byte[])reader.GetValue(index)
                        : Convert.ChangeType(reader.GetValue(index), type.IsEnum ? Enum.GetUnderlyingType(type) : type, CultureInfo.InvariantCulture);
                    row[column.Name] = JsonSerializer.SerializeToNode(value);
                }
                rows.Add(row);
            }
            result[relation.Tag] = rows;
        }
        return result;
    }

    private string SqlitePredicate(Relation relation, JsonObject scope, System.Data.Common.DbCommand command)
    {
        string Param(object? value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "p" + command.Parameters.Count;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
            return "@" + parameter.ParameterName;
        }
        string In(string field, IEnumerable<object?> values)
        {
            string[] parameters = values.Select(Param).ToArray();
            return parameters.Length == 0 ? "0 = 1" : $"{field} IN ({string.Join(", ", parameters)})";
        }
        IEnumerable<object?> Ids(string tag) => scope[tag]!.AsArray().Select(value => (object?)value!.GetValue<Guid>());
        var tenants = Relations.Single(item => item.Tag == "Tenants");
        string selectedTenants = $"SELECT {tenants.Field("Id")} FROM {tenants.Table} WHERE " +
            In(tenants.Field("Slug"), scope["Slugs"]!.AsArray().Select(value => (object?)value!.GetValue<string>()));
        var policies = Relations.Single(item => item.Tag == "Policies");
        string policyRoots = $"SELECT {policies.Field("Id")} FROM {policies.Table} WHERE {policies.Field("IsActive")} = 1 OR " +
            In(policies.Field("Id"), Ids("Policies"));
        return relation.Tag switch
        {
            "Systems" => In(relation.Field("SettingKey"), SystemReadKeys),
            "Tenants" => $"({relation.Field("Id")} IN ({selectedTenants}) OR {In(relation.Field("Id"), Ids("Tenants"))})",
            "Overrides" => $"({In(relation.Field("SettingKey"), CrossTenantKeys)} OR " +
                $"({relation.Field("TenantId")} IN ({selectedTenants}) OR {In(relation.Field("TenantId"), Ids("Tenants"))}) " +
                $"AND {In(relation.Field("SettingKey"), ConfigurationManifestCatalog.TenantSettings.Keys)})",
            "Documents" => $"({relation.Field("TenantId")} IN ({selectedTenants}) OR {In(relation.Field("TenantId"), Ids("Tenants"))} " +
                $"OR {In(relation.Field("Id"), Ids("Documents"))}) AND {In(relation.Field("DocumentKey"), new[] { SettingsDocumentKeys.Tenant.Branding, SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity })}",
            "Policies" => $"{relation.Field("IsActive")} = 1 OR {In(relation.Field("Id"), Ids("Policies"))}",
            "Organizers" or "Currencies" or "Refunds" or "Limits" => $"{relation.Field("PaidEventPolicyVersionId")} IN ({policyRoots})",
            "Operations" => $"{In(relation.Field("Id"), Ids("Operations"))} OR {relation.Field("Id")} = " +
                $"(SELECT {relation.Field("Id")} FROM {relation.Table} WHERE {relation.Field("Status")} = {Param(ProviderScalar("Operations", nameof(ConfigurationManifestOperation.Status), ConfigurationManifestOperationStatus.Applied))} " +
                $"AND {relation.Field("BootstrapGeneration")} IS NOT NULL ORDER BY {relation.Field("BootstrapGeneration")} DESC, " +
                $"{relation.Field("CompletedAt")} DESC, {relation.Field("Id")} DESC LIMIT 1)",
            "Results" => $"{relation.Field("OperationId")} = {Param(scope["Operation"]!.GetValue<Guid>())}",
            "Outbox" => $"{relation.Field("Id")} = {Param(scope["Effect"]!.GetValue<Guid>())} " +
                $"AND {relation.Field("AggregateId")} = {Param(scope["Operation"]!.GetValue<Guid>())} " +
                $"AND {relation.Field("AggregateType")} = {Param(ConfigurationManifestEffectOutbox.AggregateType)} " +
                $"AND {relation.Field("EventType")} = {Param(ConfigurationManifestEffectOutbox.EventType)}",
            _ => throw new InvalidOperationException("Unknown bootstrap relation.")
        };
    }
}
