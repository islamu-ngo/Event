using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Executes support source reads and provider locks on the caller's original physical owner.
/// </summary>
internal sealed class HumanSupportAccessSourceQueries(
    ExploreDbContext _context, RelationalConnectionIdentity connection, Guid? _tenant,
    HumanSupportForwardingObservation _forwarding, PolicySourceSetPurpose _purpose,
    PostgresNativeQueueEnrollment? _enrollment)
{
    private readonly DbConnection _connection = connection.Connection;
    private static readonly JsonSerializerOptions Json = new() { AllowDuplicateProperties = false };
    private sealed record NativeSetting(Guid Id, string SettingKey, string Value, int SettingValueTypeId,
        bool IsLocked, string? Description, string? Category, JsonElement AllowedValues);

    private DbCommand Command(DbTransaction? transaction)
    {
        var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = _context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        return command;
    }

    private void Parameters(DbCommand command)
    {
        void Add(string name, object? value, DbType type)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.DbType = type;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        Add("actor", _forwarding.Subject.PrincipalId, DbType.Guid);
        Add("tenant", _tenant, DbType.Guid);
        Add("session", _forwarding.SessionId, DbType.Guid);
        Add("resolved", _forwarding.ResolvedTenantId, DbType.Guid);
        if (_context.Database.IsNpgsql()) Add("purpose", (int)_purpose, DbType.Int32);
    }

    internal async Task<HumanSupportAccessSourceFacts> ReadAsync(
        DbTransaction? transaction, Action requireSelection, CancellationToken ct)
    {
        if (_context.Database.IsNpgsql())
        {
            await using var command = Command(transaction);
            command.CommandText = PostgresHumanSupportAccessSourceContract.AdmissionSql(_context, _enrollment!);
            await command.ExecuteNonQueryAsync(ct);
            command.CommandText = $"SELECT {(transaction is null
                ? PostgresHumanSupportAccessSourceContract.ObserveFunctionSql(_context)
                : PostgresHumanSupportAccessSourceContract.AcquireFunctionSql(_context))}(@actor,@purpose,@tenant,@session,@resolved)";
            Parameters(command);
            using var result = JsonDocument.Parse((string)(await command.ExecuteScalarAsync(ct)
                ?? throw new InvalidOperationException("human_support_native_facts_missing")));
            command.Parameters.Clear();
            command.CommandText = PostgresHumanSupportAccessSourceContract.AdmissionSql(_context, _enrollment!);
            await command.ExecuteNonQueryAsync(ct);
            requireSelection();
            return new(_forwarding,
                result.RootElement.GetProperty("q0").Deserialize<ImmutableArray<HumanSupportSessionFact>>(Json),
                result.RootElement.GetProperty("q1").Deserialize<ImmutableArray<NativeSetting>>(Json)
                    .Select(row => new HumanSupportSettingFact(row.Id, row.SettingKey, row.Value,
                        row.SettingValueTypeId, row.IsLocked, row.Description, row.Category,
                        row.AllowedValues.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                            ? null : row.AllowedValues.GetRawText())).ToImmutableArray());
        }
        bool current = transaction is not null && _context.Database.ProviderName == RelationalNamedLock.MySqlProvider;
        string[] queries = new HumanSupportAccessSourceSql(_context).Queries;
        var sessions = await RowsAsync<HumanSupportSessionFact>(queries[0] + (current ? " FOR UPDATE" : ""), transaction, ct);
        var settings = await RowsAsync<HumanSupportSettingFact>(queries[1] + (current ? " FOR UPDATE" : ""), transaction, ct);
        requireSelection();
        return new(_forwarding, sessions, settings);
    }

    private async Task<ImmutableArray<T>> RowsAsync<T>(string sql, DbTransaction? transaction, CancellationToken ct)
    {
        await using var command = Command(transaction);
        command.CommandText = sql;
        Parameters(command);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = ImmutableArray.CreateBuilder<T>();
        while (await reader.ReadAsync(ct))
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (int index = 0; index < reader.FieldCount; index++)
                values.Add(reader.GetName(index), await reader.IsDBNullAsync(index, ct) ? null : reader.GetValue(index));
            foreach (var property in typeof(T).GetProperties())
            {
                if (!values.TryGetValue(property.Name, out object? value) || value is null) continue;
                if (property.PropertyType == typeof(Guid) || property.PropertyType == typeof(Guid?))
                    values[property.Name] = value is Guid guid ? guid : Guid.Parse((string)value);
                if (property.PropertyType == typeof(bool))
                    values[property.Name] = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                if (property.PropertyType == typeof(DateTimeOffset) || property.PropertyType == typeof(DateTimeOffset?))
                    values[property.Name] = value switch
                    {
                        DateTimeOffset offset => offset,
                        DateTime date => new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)),
                        string text => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
                        _ => throw new InvalidOperationException("human_support_time_storage_invalid")
                    };
            }
            rows.Add(JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(values), Json)
                ?? throw new InvalidOperationException("human_support_fact_row_invalid"));
        }
        return rows.ToImmutable();
    }

    internal async Task LockPortableAsync(DbTransaction transaction, CancellationToken ct)
    {
        var sql = new HumanSupportAccessSourceSql(_context);
        if (_context.Database.IsSqlite())
        {
            await using var command = Command(transaction);
            string column = sql.Names.Column(typeof(SupportAccessSession), "Id");
            command.CommandText = $"UPDATE {sql.Names.Table(typeof(SupportAccessSession))} SET {column} = {column} WHERE 0";
            await command.ExecuteNonQueryAsync(ct);
            return;
        }
        bool sqlServer = _context.Database.IsSqlServer();
        bool mysql = _context.Database.ProviderName == RelationalNamedLock.MySqlProvider;
        if (!sqlServer && !mysql) throw new NotSupportedException("human_support_provider_not_supported");
        foreach (var source in HumanSupportAccessSourceSql.Sources)
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
                    throw new InvalidOperationException("human_support_requires_innodb_sources");
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
