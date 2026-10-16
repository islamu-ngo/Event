using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Executes the fixed route projection on the caller's captured connection and transaction.
/// </summary>
internal sealed class HumanAuthorizationRouteQueries(
    ExploreDbContext _context, HumanCommitSubject _subject, PolicySourceSetPurpose _purpose,
    PostgresNativeQueueEnrollment? _enrollment, RelationalConnectionIdentity connection, Guid? _tenant)
{
    private readonly DbConnection _connection = connection.Connection;
    private static readonly JsonSerializerOptions Json = new() { AllowDuplicateProperties = false };

    internal async Task<HumanAuthorizationRouteFacts> ReadAsync(DbTransaction? transaction, CancellationToken ct)
    {
        if (_context.Database.IsNpgsql())
        {
            await using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = _context.Database.GetCommandTimeout() ?? command.CommandTimeout;
            async Task AttestAsync()
            {
                command.Parameters.Clear();
                command.CommandText = PostgresPolicySourceSetContract.AdmissionSql(_context, _enrollment!)
                    + PostgresHumanAuthorizationRouteContract.AdmissionSql(_context, _enrollment!);
                await command.ExecuteNonQueryAsync(ct);
            }
            await AttestAsync();
            string routine = transaction is null
                ? PostgresHumanAuthorizationRouteContract.ObserveFunctionSql(_context)
                : PostgresHumanAuthorizationRouteContract.AcquireFunctionSql(_context);
            command.CommandText = $"SELECT {routine}(@purpose,@tenant)";
            command.Parameters.Add(new NpgsqlParameter("purpose", NpgsqlDbType.Integer) { Value = (int)_purpose });
            command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = (object?)_tenant ?? DBNull.Value });
            string json = (string)(await command.ExecuteScalarAsync(ct)
                ?? throw new InvalidOperationException("human_route_projection_missing"));
            var rows = JsonSerializer.Deserialize<NativeRows>(json, Json)
                ?? throw new InvalidOperationException("human_route_projection_invalid");
            await AttestAsync();
            return Facts(rows.System.Select(row => new NativeSystem(row.Key, row.Value,
                row.ValueType, row.IsLocked, row.Description, row.Category,
                row.AllowedValues.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                    ? null : row.AllowedValues.GetRawText())).ToArray(), rows.Tenant);
        }
        if (!_context.Database.IsSqlite() && !_context.Database.IsSqlServer()
            && _context.Database.ProviderName != RelationalNamedLock.MySqlProvider)
            throw new NotSupportedException("human_route_provider_not_supported");
        // Current reads matter on MySQL: a snapshot established earlier in this owner is not authority.
        var sql = new HumanAuthorizationRouteSql(_context);
        await using var read = _connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandTimeout = _context.Database.GetCommandTimeout() ?? read.CommandTimeout;
        for (int index = 0; index < HumanAuthorizationRouteFacts.Keys.Length; index++)
        {
            var parameter = read.CreateParameter();
            parameter.ParameterName = $"key{index}";
            parameter.DbType = DbType.String;
            parameter.Value = HumanAuthorizationRouteFacts.Keys[index];
            read.Parameters.Add(parameter);
        }
        var tenantParameter = read.CreateParameter();
        tenantParameter.ParameterName = "tenant";
        tenantParameter.DbType = DbType.Guid;
        tenantParameter.Value = (object?)_tenant ?? DBNull.Value;
        read.Parameters.Add(tenantParameter);
        var system = new List<NativeSystem>();
        var tenant = new List<NativeTenant>();
        read.CommandText = sql.Query(false, transaction is not null);
        await using (var rows = await read.ExecuteReaderAsync(ct))
        {
            while (await rows.ReadAsync(ct))
                system.Add(new(rows.GetString(0), rows.GetString(1), rows.GetInt32(2),
                    Convert.ToBoolean(rows.GetValue(3), System.Globalization.CultureInfo.InvariantCulture),
                    rows.IsDBNull(4) ? null : rows.GetString(4), rows.IsDBNull(5) ? null : rows.GetString(5),
                    rows.IsDBNull(6) ? null : rows.GetString(6)));
        }
        if (_tenant.HasValue)
        {
            read.CommandText = sql.Query(true, transaction is not null);
            await using var rows = await read.ExecuteReaderAsync(ct);
            while (await rows.ReadAsync(ct))
                tenant.Add(new(rows.GetString(0), rows.GetString(1),
                    Convert.ToBoolean(rows.GetValue(2), System.Globalization.CultureInfo.InvariantCulture)));
        }
        return Facts(system.ToArray(), tenant.ToArray());
    }

    private HumanAuthorizationRouteFacts Facts(NativeSystem[] system, NativeTenant[] tenant)
    {
        if (system.Any(row => !HumanAuthorizationRouteFacts.Keys.Contains(row.Key))
            || tenant.Any(row => !HumanAuthorizationRouteFacts.Keys.Contains(row.Key))
            || (!_tenant.HasValue && tenant.Length != 0))
            throw new InvalidOperationException("human_route_projection_scope_invalid");
        // Duplicate keys fail instead of choosing an arbitrary row.
        var systemByKey = system.ToDictionary(row => row.Key, StringComparer.Ordinal);
        var tenantByKey = tenant.ToDictionary(row => row.Key, StringComparer.Ordinal);
        return new(_subject.PrincipalId, _tenant, HumanAuthorizationRouteFacts.Keys.Select(key =>
        {
            systemByKey.TryGetValue(key, out var instance);
            tenantByKey.TryGetValue(key, out var scoped);
            return new HumanAuthorizationRouteSettingFact(key,
                instance is null ? null : new SystemSettingFacts(instance.Value, (SettingValueType)instance.ValueType,
                    instance.IsLocked, instance.Description, instance.Category, instance.AllowedValues),
                scoped is null ? null : new TenantSettingFacts(scoped.Value, scoped.IsLocked));
        }).ToImmutableArray());
    }

    private sealed record NativeRows(NativeJsonSystem[] System, NativeTenant[] Tenant);
    private sealed record NativeJsonSystem(string Key, string Value, int ValueType, bool IsLocked,
        string? Description, string? Category, JsonElement AllowedValues);
    private sealed record NativeSystem(string Key, string Value, int ValueType, bool IsLocked,
        string? Description, string? Category, string? AllowedValues);
    private sealed record NativeTenant(string Key, string Value, bool IsLocked);

}
