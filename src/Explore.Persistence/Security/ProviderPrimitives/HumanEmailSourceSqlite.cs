using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

/// <summary>
/// SQLite has no SQL login roles. Its source authority is the exact bound email file connection
/// and original Serializable write reservation, not a fictitious PostgreSQL ACL attestation.
/// </summary>
internal static class HumanEmailSourceSqlite
{
    internal static async Task<HumanEmailSourceFacts> ReadAsync(ExploreDbContext context, DbTransaction? transaction,
        HumanCommitSubject subject, HumanAuthenticationLifetime lifetime, HumanSupportForwardingObservation forwarding,
        Guid? tenant, bool route, CancellationToken ct)
    {
        if (!context.Database.IsSqlite())
            throw new NotSupportedException("human_email_sqlite_owner_required");
        var sql = new HumanPrincipalSourceSql(context);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        if (transaction is not null)
        {
            // Same complete-database write reservation as PolicySourceSetFence. Even no matching
            // row upgrades the native transaction; stale WAL snapshots fail instead of certifying.
            string role = sql.Column(typeof(RolePermission), nameof(RolePermission.RoleId));
            command.CommandText = $"UPDATE {sql.Table(typeof(RolePermission))} SET {role} = {role} WHERE 0";
            await command.ExecuteNonQueryAsync(ct);
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
        Add("tenant", tenant, DbType.Guid);
        Add("stamp", lifetime.LocalSession?.SecurityStamp, DbType.String);
        Add("session", forwarding.SessionId, DbType.Guid);
        Add("resolved", forwarding.ResolvedTenantId, DbType.Guid);
        for (int index = 0; index < HumanAuthorizationRouteFacts.Keys.Length; index++)
            Add($"key{index}", HumanAuthorizationRouteFacts.Keys[index], DbType.String);
        async Task<JsonArray> Rows(string query, Type type)
        {
            command.CommandText = query;
            await using var reader = await command.ExecuteReaderAsync(ct);
            var result = new JsonArray();
            while (await reader.ReadAsync(ct))
            {
                var row = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (int index = 0; index < reader.FieldCount; index++)
                    row.Add(reader.GetName(index), await reader.IsDBNullAsync(index, ct) ? null : reader.GetValue(index));
                foreach (var property in type.GetProperties())
                {
                    if (!row.TryGetValue(property.Name, out object? value) || value is null) continue;
                    if (property.PropertyType == typeof(bool))
                        row[property.Name] = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                    if (property.PropertyType == typeof(Guid) || property.PropertyType == typeof(Guid?))
                        row[property.Name] = value is Guid guid ? guid : Guid.Parse((string)value);
                    if (property.PropertyType == typeof(DateTime) || property.PropertyType == typeof(DateTime?))
                        row[property.Name] = DateTime.SpecifyKind(value is DateTime date ? date
                            : DateTime.Parse((string)value, CultureInfo.InvariantCulture), DateTimeKind.Utc);
                    if (property.PropertyType == typeof(DateTimeOffset) || property.PropertyType == typeof(DateTimeOffset?))
                        row[property.Name] = value is DateTimeOffset offset ? offset
                            : DateTimeOffset.Parse((string)value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
                }
                result.Add(JsonSerializer.SerializeToNode(row));
            }
            return result;
        }
        Type[] principalTypes =
        [
            typeof(HumanPrincipalAccountFact), typeof(HumanPrincipalBindingFact),
            typeof(HumanPrincipalPlatformGrantFact), typeof(HumanPrincipalRoleFact),
            typeof(HumanPrincipalTenantMembershipFact), typeof(HumanPrincipalTenantGrantFact),
            typeof(HumanPrincipalTenantFact), typeof(HumanPrincipalOwnerMembershipFact),
            typeof(HumanPrincipalOwnerMembershipFact), typeof(HumanPrincipalPermissionFact)
        ];
        var p = new JsonObject();
        string[] principal = sql.Queries;
        for (int index = 0; index < principalTypes.Length; index++)
            p[$"q{index}"] = await Rows(principal[index], principalTypes[index]);
        bool mappingsExist = (await Rows(principal[10], typeof(IntValue))).Count != 0;
        p["PermissionMappingsExist"] = mappingsExist;
        for (int index = 11; index < principal.Length; index++)
        {
            var rows = await Rows(principal[index], typeof(GuidValue));
            if (rows.Count == 0 && !mappingsExist)
                rows = await Rows(sql.FallbackQueries[(index - 11) % 2], typeof(GuidValue));
            p[$"q{index}"] = rows;
        }
        if (lifetime.LocalSession is not null)
        {
            Type[] localTypes = [typeof(HumanPrincipalLocalAccountFact), typeof(StringValue),
                typeof(HumanPrincipalLocalOperationFact), typeof(HumanPrincipalPersonalActorFact), typeof(StringValue)];
            string[] local = sql.LocalQueries;
            for (int index = 0; index < local.Length; index++)
                p[$"l{index}"] = await Rows(local[index], localTypes[index]);
        }
        JsonObject? selectedRoute = null;
        if (route)
        {
            var routes = new HumanAuthorizationRouteSql(context);
            selectedRoute = new()
            {
                ["System"] = await Rows(routes.Query(false), typeof(RouteRow)),
                ["Tenant"] = tenant.HasValue ? await Rows(routes.Query(true), typeof(RouteRow)) : new JsonArray()
            };
        }
        if (transaction is not null)
        {
            string id = sql.Column(typeof(SupportAccessSession), nameof(SupportAccessSession.Id));
            command.CommandText = $"UPDATE {sql.Table(typeof(SupportAccessSession))} SET {id} = {id} WHERE 0";
            await command.ExecuteNonQueryAsync(ct);
        }
        string[] support = new HumanSupportAccessSourceSql(context).Queries;
        var h = new JsonObject
        {
            ["q0"] = await Rows(support[0], typeof(HumanSupportSessionFact)),
            ["q1"] = await Rows(support[1], typeof(HumanSupportSettingFact))
        };
        return HumanEmailSourceProjection.Read(new JsonObject
        {
            ["Principal"] = p,
            ["Support"] = h,
            ["Route"] = selectedRoute
        }.ToJsonString(), subject, tenant, forwarding, lifetime.LocalSession is not null, route);
    }

    private sealed record IntValue(int Value);
    private sealed record GuidValue(Guid Value);
    private sealed record StringValue(string? Value);
    private sealed record RouteRow(bool IsLocked);
}
