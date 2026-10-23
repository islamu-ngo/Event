using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

internal sealed class HumanEventAssignmentSourceQueries(
    ExploreDbContext _context, RelationalConnectionIdentity connection, Guid _tenant, Guid _human,
    ImmutableArray<Guid> _events, PolicySourceSetPurpose _purpose, PostgresNativeQueueEnrollment? _enrollment)
{
    private readonly DbConnection _connection = connection.Connection;
    private static readonly JsonSerializerOptions Json = new() { AllowDuplicateProperties = false };

    private DbCommand Command(DbTransaction? transaction)
    {
        var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = _context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        return command;
    }

    private void Parameters(DbCommand command)
    {
        void Add(string name, object value, DbType type)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.DbType = type;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
        Add("actor", _human, DbType.Guid);
        Add("tenant", _tenant, DbType.Guid);
        if (_context.Database.IsNpgsql())
        {
            command.Parameters.Add(new NpgsqlParameter("events", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
            { Value = _events.ToArray() });
            Add("purpose", (int)_purpose, DbType.Int32);
        }
        else
            for (int index = 0; index < _events.Length; index++) Add($"event{index}", _events[index], DbType.Guid);
    }

    internal async Task<HumanEventAssignmentSourceFacts> ReadAsync(
        DbTransaction? transaction, Action requireSelection, CancellationToken ct)
    {
        JsonDocument? native = null;
        try
        {
            if (_context.Database.IsNpgsql())
            {
                await using var command = Command(transaction);
                command.CommandText = PostgresHumanEventAssignmentSourceContract.AdmissionSql(_context, _enrollment!);
                await command.ExecuteNonQueryAsync(ct);
                command.CommandText = $"SELECT {(transaction is null
                    ? PostgresHumanEventAssignmentSourceContract.ObserveFunctionSql(_context)
                    : PostgresHumanEventAssignmentSourceContract.AcquireFunctionSql(_context))}(@actor,@purpose,@tenant,@events)";
                Parameters(command);
                native = JsonDocument.Parse((string)(await command.ExecuteScalarAsync(ct)
                    ?? throw new InvalidOperationException("human_assignment_native_result_missing")));
                command.Parameters.Clear();
                command.CommandText = PostgresHumanEventAssignmentSourceContract.AdmissionSql(_context, _enrollment!);
                await command.ExecuteNonQueryAsync(ct);
            }
            bool current = transaction is not null && _context.Database.ProviderName == RelationalNamedLock.MySqlProvider;
            string[] queries = new HumanEventAssignmentSourceSql(_context, _events.Length, current).Queries;
            async Task<ImmutableArray<T>> Rows<T>(int index) => native is not null
                ? native.RootElement.GetProperty($"q{index}").Deserialize<ImmutableArray<T>>(Json)
                : await ReadRowsAsync<T>(queries[index] + (current ? " FOR UPDATE" : ""), transaction, ct);
            var events = await Rows<HumanAssignmentEventFact>(0);
            var assignments = await Rows<HumanEventAssignmentFact>(1);
            var roles = await Rows<HumanAssignmentRoleFact>(2);
            var permissions = await Rows<HumanAssignmentPermissionFact>(3);
            var actors = await Rows<HumanAssignmentActorFact>(4);
            var members = await Rows<HumanAssignmentTenantMemberFact>(5);
            var organizations = await Rows<HumanAssignmentOwnerMemberFact>(6);
            var groups = await Rows<HumanAssignmentOwnerMemberFact>(7);
            var provenance = await Rows<HumanAssignmentProvenanceFact>(8);
            requireSelection();
            return new(_human, _tenant, _events, events, assignments, roles, permissions,
                actors, members, organizations, groups, provenance);
        }
        finally { native?.Dispose(); }
    }

    private async Task<ImmutableArray<T>> ReadRowsAsync<T>(string sql, DbTransaction? transaction, CancellationToken ct)
    {
        await using var command = Command(transaction);
        command.CommandText = sql;
        Parameters(command);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = ImmutableArray.CreateBuilder<T>();
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (int index = 0; index < reader.FieldCount; index++)
                row.Add(reader.GetName(index), await reader.IsDBNullAsync(index, ct) ? null : reader.GetValue(index));
            foreach (var property in typeof(T).GetProperties())
            {
                if (!row.TryGetValue(property.Name, out object? value) || value is null) continue;
                if (property.PropertyType == typeof(bool)) row[property.Name] = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                if (property.PropertyType == typeof(Guid) || property.PropertyType == typeof(Guid?))
                    row[property.Name] = value is Guid guid ? guid : Guid.Parse((string)value);
                if (property.PropertyType == typeof(DateTime) || property.PropertyType == typeof(DateTime?))
                    row[property.Name] = DateTime.SpecifyKind(value is DateTime date ? date
                        : DateTime.Parse((string)value, CultureInfo.InvariantCulture), DateTimeKind.Utc);
            }
            result.Add(JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(row), Json)
                ?? throw new InvalidOperationException("human_assignment_row_invalid"));
        }
        return result.ToImmutable();
    }

    internal async Task LockPortableAsync(DbTransaction transaction, CancellationToken ct)
    {
        var sql = new HumanEventAssignmentSourceSql(_context, _events.Length);
        if (_context.Database.IsSqlite())
        {
            await using var command = Command(transaction);
            string column = sql.Names.Column(typeof(Explore.Domain.EventRoleAssignment), "Id");
            command.CommandText = $"UPDATE {sql.Names.Table(typeof(Explore.Domain.EventRoleAssignment))} SET {column} = {column} WHERE 0";
            await command.ExecuteNonQueryAsync(ct);
            return;
        }
        bool sqlServer = _context.Database.IsSqlServer();
        bool mysql = _context.Database.ProviderName == RelationalNamedLock.MySqlProvider;
        if (!sqlServer && !mysql) throw new NotSupportedException("human_assignment_provider_not_supported");
        foreach (var source in HumanEventAssignmentSourceSql.Sources)
        {
            await using var command = Command(transaction);
            if (mysql)
            {
                command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() "
                    + "AND table_name = @table AND engine = 'InnoDB' AND table_type = 'BASE TABLE'";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "table";
                parameter.Value = _context.Model.FindEntityType(source.Type)!.GetTableName()!;
                command.Parameters.Add(parameter);
                if (Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) != 1)
                    throw new InvalidOperationException("human_assignment_requires_innodb_sources");
                command.Parameters.Clear();
            }
            command.CommandText = sqlServer
                ? $"SELECT 1 FROM {sql.Names.Table(source.Type)} WITH (TABLOCKX,HOLDLOCK)"
                : $"SELECT 1 FROM {sql.Names.Table(source.Type)} ORDER BY {sql.Names.Column(source.Type, source.Columns[0])} FOR UPDATE";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) { }
        }
    }
}
