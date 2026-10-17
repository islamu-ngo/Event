using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

/// <summary>Fixed complete source projections on the original dedicated platform-key connection.</summary>
internal static class PlatformApiKeySourceProjection
{
    private static readonly JsonSerializerOptions Json = new() { AllowDuplicateProperties = false };

    internal static PlatformApiKeySourceFacts Read(string json, HumanCommitSubject subject,
        HumanAuthenticationLifetime lifetime, HumanSupportForwardingObservation forwarding)
    {
        using var document = JsonDocument.Parse(json);
        var p = document.RootElement.GetProperty("Principal");
        ImmutableArray<T> Rows<T>(string key) => p.GetProperty(key).Deserialize<ImmutableArray<T>>(Json);
        HumanPrincipalLocalSessionFacts? local = null;
        if (lifetime.LocalSession is not null)
        {
            var accounts = Rows<HumanPrincipalLocalAccountFact>("l0");
            string? metadata = Rows<StringValue>("l1").SingleOrDefault()?.Value;
            LocalCredentialStateMetadata? state;
            try { state = string.IsNullOrWhiteSpace(metadata) ? null : JsonSerializer.Deserialize<LocalCredentialStateMetadata>(metadata, Json); }
            catch (JsonException) { state = null; }
            catch (ArgumentException) { state = null; }
            var deliveryRows = Rows<StringValue>("l4");
            string? delivery = accounts is [var account] && !account.EmailConfirmed
                ? deliveryRows.SingleOrDefault()?.Value : null;
            local = new(accounts, state, Rows<HumanPrincipalLocalOperationFact>("l2"),
                Rows<HumanPrincipalPersonalActorFact>("l3"), delivery is null ? null : JsonSerializer.Deserialize<bool>(delivery));
        }
        Guid? policyTenant = forwarding.ResolvedTenantId;
        var principal = new HumanPrincipalSourceFacts(subject.PrincipalId, policyTenant,
            Rows<HumanPrincipalAccountFact>("q0"), Rows<HumanPrincipalBindingFact>("q1"),
            Rows<HumanPrincipalPlatformGrantFact>("q2"), Rows<HumanPrincipalRoleFact>("q3"),
            Rows<HumanPrincipalTenantMembershipFact>("q4"), Rows<HumanPrincipalTenantGrantFact>("q5"),
            Rows<HumanPrincipalTenantFact>("q6"), Rows<HumanPrincipalOwnerMembershipFact>("q7"),
            Rows<HumanPrincipalOwnerMembershipFact>("q8"), Rows<HumanPrincipalPermissionFact>("q9"),
            p.GetProperty("PermissionMappingsExist").GetBoolean(),
            Rows<GuidValue>("q11").Select(row => row.Value).ToImmutableArray(),
            Rows<GuidValue>("q12").Select(row => row.Value).ToImmutableArray(),
            Rows<GuidValue>("q13").Select(row => row.Value).ToImmutableArray(),
            Rows<GuidValue>("q14").Select(row => row.Value).ToImmutableArray(), local);
        var routeJson = document.RootElement.GetProperty("Route");
        var r = routeJson.GetProperty("System").Deserialize<ImmutableArray<RouteRow>>(Json);
        var t = routeJson.GetProperty("Tenant").Deserialize<ImmutableArray<TenantRouteRow>>(Json);
        if (r.Any(row => !HumanAuthorizationRouteFacts.Keys.Contains(row.Key))
            || t.Any(row => !HumanAuthorizationRouteFacts.Keys.Contains(row.Key)) || !policyTenant.HasValue && !t.IsEmpty)
            throw new InvalidOperationException("platform_key_source_route_invalid");
        var byKey = r.ToDictionary(row => row.Key, StringComparer.Ordinal);
        var tenantByKey = t.ToDictionary(row => row.Key, StringComparer.Ordinal);
        var route = new HumanAuthorizationRouteFacts(subject.PrincipalId, policyTenant,
            HumanAuthorizationRouteFacts.Keys.Select(key =>
            {
                byKey.TryGetValue(key, out var row);
                tenantByKey.TryGetValue(key, out var tenantRow);
                return new HumanAuthorizationRouteSettingFact(key, row is null ? null :
                    new SystemSettingFacts(row.Value, (SettingValueType)row.ValueType, row.IsLocked,
                        row.Description, row.Category, row.AllowedValues),
                    tenantRow is null ? null : new TenantSettingFacts(tenantRow.Value, tenantRow.IsLocked));
            }).ToImmutableArray());
        var h = document.RootElement.GetProperty("Support");
        return new(principal, route, new(forwarding,
            h.GetProperty("q0").Deserialize<ImmutableArray<HumanSupportSessionFact>>(Json),
            h.GetProperty("q1").Deserialize<ImmutableArray<HumanSupportSettingFact>>(Json)));
    }

    internal static async Task<PlatformApiKeySourceFacts> ReadSqliteAsync(ExploreDbContext context,
        DbTransaction? transaction, HumanCommitSubject subject, HumanAuthenticationLifetime lifetime,
        HumanSupportForwardingObservation forwarding, CancellationToken token)
    {
        if (!context.Database.IsSqlite()) throw new NotSupportedException("platform_key_sqlite_owner_required");
        var sql = new HumanPrincipalSourceSql(context);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        if (transaction is not null)
        {
            string role = sql.Column(typeof(RolePermission), nameof(RolePermission.RoleId));
            command.CommandText = $"UPDATE {sql.Table(typeof(RolePermission))} SET {role} = {role} WHERE 0";
            await command.ExecuteNonQueryAsync(token);
        }
        void Add(string name, object? value, DbType type)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.DbType = type;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        Add("actor", subject.PrincipalId, DbType.Guid);
        Add("provider", (int)subject.AccountKey!.ProviderKind, DbType.Int32);
        Add("account", subject.AccountKey.Value, DbType.String);
        Add("tenant", forwarding.ResolvedTenantId, DbType.Guid);
        Add("stamp", lifetime.LocalSession?.SecurityStamp, DbType.String);
        Add("session", forwarding.SessionId, DbType.Guid);
        Add("resolved", forwarding.ResolvedTenantId, DbType.Guid);
        for (int index = 0; index < HumanAuthorizationRouteFacts.Keys.Length; index++)
            Add($"key{index}", HumanAuthorizationRouteFacts.Keys[index], DbType.String);
        async Task<JsonArray> Rows(string query, Type type)
        {
            command.CommandText = query;
            await using var reader = await command.ExecuteReaderAsync(token);
            var result = new JsonArray();
            while (await reader.ReadAsync(token))
            {
                var row = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (int index = 0; index < reader.FieldCount; index++)
                    row.Add(reader.GetName(index), await reader.IsDBNullAsync(index, token) ? null : reader.GetValue(index));
                foreach (var property in type.GetProperties())
                {
                    if (!row.TryGetValue(property.Name, out object? value) || value is null) continue;
                    if (property.PropertyType == typeof(bool))
                        row[property.Name] = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                    if (property.PropertyType == typeof(Guid) || property.PropertyType == typeof(Guid?))
                        row[property.Name] = value is Guid guid ? guid : Guid.Parse((string)value);
                    if (property.PropertyType == typeof(DateTime) || property.PropertyType == typeof(DateTime?))
                        row[property.Name] = DateTime.SpecifyKind(value is DateTime date ? date :
                            DateTime.Parse((string)value, CultureInfo.InvariantCulture), DateTimeKind.Utc);
                    if (property.PropertyType == typeof(DateTimeOffset) || property.PropertyType == typeof(DateTimeOffset?))
                        row[property.Name] = value is DateTimeOffset offset ? offset :
                            DateTimeOffset.Parse((string)value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
                }
                result.Add(JsonSerializer.SerializeToNode(row));
            }
            return result;
        }
        Type[] principalTypes =
        [
            typeof(HumanPrincipalAccountFact), typeof(HumanPrincipalBindingFact), typeof(HumanPrincipalPlatformGrantFact),
            typeof(HumanPrincipalRoleFact), typeof(HumanPrincipalTenantMembershipFact), typeof(HumanPrincipalTenantGrantFact),
            typeof(HumanPrincipalTenantFact), typeof(HumanPrincipalOwnerMembershipFact),
            typeof(HumanPrincipalOwnerMembershipFact), typeof(HumanPrincipalPermissionFact)
        ];
        var p = new JsonObject();
        string[] queries = sql.Queries;
        for (int index = 0; index < principalTypes.Length; index++)
            p[$"q{index}"] = await Rows(queries[index], principalTypes[index]);
        bool mappings = (await Rows(queries[10], typeof(IntValue))).Count != 0;
        p["PermissionMappingsExist"] = mappings;
        for (int index = 11; index < queries.Length; index++)
        {
            var rows = await Rows(queries[index], typeof(GuidValue));
            if (rows.Count == 0 && !mappings) rows = await Rows(sql.FallbackQueries[(index - 11) % 2], typeof(GuidValue));
            p[$"q{index}"] = rows;
        }
        if (lifetime.LocalSession is not null)
        {
            Type[] types = [typeof(HumanPrincipalLocalAccountFact), typeof(StringValue),
                typeof(HumanPrincipalLocalOperationFact), typeof(HumanPrincipalPersonalActorFact), typeof(StringValue)];
            for (int index = 0; index < types.Length; index++)
                p[$"l{index}"] = await Rows(sql.LocalQueries[index], types[index]);
        }
        var routeSql = new HumanAuthorizationRouteSql(context);
        var route = new JsonObject
        {
            ["System"] = await Rows(routeSql.Query(false), typeof(RouteRow)),
            ["Tenant"] = forwarding.ResolvedTenantId.HasValue
                ? await Rows(routeSql.Query(true), typeof(TenantRouteRow)) : new JsonArray()
        };
        if (transaction is not null)
        {
            string id = sql.Column(typeof(SupportAccessSession), nameof(SupportAccessSession.Id));
            command.CommandText = $"UPDATE {sql.Table(typeof(SupportAccessSession))} SET {id} = {id} WHERE 0";
            await command.ExecuteNonQueryAsync(token);
        }
        var support = new HumanSupportAccessSourceSql(context).Queries;
        return Read(new JsonObject
        {
            ["Principal"] = p,
            ["Route"] = route,
            ["Support"] = new JsonObject
            {
                ["q0"] = await Rows(support[0], typeof(HumanSupportSessionFact)),
                ["q1"] = await Rows(support[1], typeof(HumanSupportSettingFact))
            }
        }.ToJsonString(), subject, lifetime, forwarding);
    }

    private sealed record IntValue(int Value);
    private sealed record StringValue(string? Value);
    private sealed record GuidValue(Guid Value);
    private sealed record RouteRow(string Key, string Value, int ValueType, bool IsLocked,
        string? Description, string? Category, string? AllowedValues);
    private sealed record TenantRouteRow(string Key, string Value, bool IsLocked);
}
