using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Reads one proposal target through the original native capability or portable physical query.
/// </summary>
internal sealed class HumanAiModerationProposalTargetQueries(
    ExploreDbContext _context, RelationalConnectionIdentity connection, Guid _selectedTenant,
    Guid _human, Guid _event, PostgresNativeQueueEnrollment? _enrollment)
{
    private readonly DbConnection _connection = connection.Connection;
    private static readonly JsonSerializerOptions Json = new() { AllowDuplicateProperties = false };

    private DbCommand Command(DbTransaction? owner)
    {
        var command = _connection.CreateCommand();
        command.Transaction = owner;
        command.CommandTimeout = _context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        return command;
    }

    private static void Parameter(DbCommand command, string name, Guid value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = DbType.Guid;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    internal async Task<HumanEventAssignmentSourceFacts?> ReadAsync(
        DbTransaction? owner, Action requireSelection, CancellationToken token)
    {
        JsonDocument? native = null;
        try
        {
            Guid tenant;
            if (_context.Database.IsNpgsql())
            {
                await PostgresHumanAiModerationProposalTargetContract.RequireCurrentInstallationAsync(_context, _enrollment!, token);
                await using var command = Command(owner);
                command.CommandText = PostgresHumanAiModerationProposalTargetContract.AdmissionSql(_context, _enrollment!);
                await command.ExecuteNonQueryAsync(token);
                command.CommandText = $"SELECT {(owner is null ? PostgresHumanAiModerationProposalTargetContract.ObserveFunctionSql(_context)
                    : PostgresHumanAiModerationProposalTargetContract.AcquireFunctionSql(_context))}(@actor,@selected,@event)";
                command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Uuid) { Value = _human });
                command.Parameters.Add(new NpgsqlParameter("selected", NpgsqlDbType.Uuid) { Value = _selectedTenant });
                command.Parameters.Add(new NpgsqlParameter("event", NpgsqlDbType.Uuid) { Value = _event });
                object? value = await command.ExecuteScalarAsync(token);
                if (value is null or DBNull) return null;
                native = JsonDocument.Parse((string)value);
                tenant = native.RootElement.GetProperty("tenant").GetGuid();
                await PostgresHumanAiModerationProposalTargetContract.RequireCurrentInstallationAsync(_context, _enrollment!, token);
            }
            else
            {
                await using var command = Command(owner);
                Parameter(command, "actor", _human);
                command.CommandText = $"SELECT CASE WHEN {PostgresHumanAiModerationProposalTargetContract.AdministratorPredicate(_context, "@actor")} THEN 1 ELSE 0 END";
                if (Convert.ToInt32(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) != 1)
                    throw new UnauthorizedAccessException("Current persisted instance administration is required before target discovery.");
                var names = new HumanPrincipalSourceSql(_context);
                command.CommandText = $"SELECT {names.Column(typeof(Explore.Domain.Event), "TenantId")} FROM {names.Table(typeof(Explore.Domain.Event))}"
                    + $" WHERE {names.Column(typeof(Explore.Domain.Event), "Id")} = @event AND {names.Column(typeof(Explore.Domain.Event), "IsDeleted")} = 0";
                Parameter(command, "event", _event);
                object? value = await command.ExecuteScalarAsync(token);
                if (value is null or DBNull) return null;
                tenant = value is Guid id ? id : Guid.Parse((string)value);
            }
            if (tenant == _selectedTenant || tenant == Guid.Empty) return null;
            bool current = owner is not null && _context.Database.ProviderName == RelationalNamedLock.MySqlProvider;
            var queries = new HumanEventAssignmentSourceSql(_context, 1, current).Queries;
            async Task<ImmutableArray<T>> Rows<T>(int index) => native is not null
                ? native.RootElement.GetProperty($"q{index}").Deserialize<ImmutableArray<T>>(Json)
                : await ReadRowsAsync<T>(queries[index] + (current ? " FOR UPDATE" : ""), owner, tenant, token);
            var facts = new HumanEventAssignmentSourceFacts(_human, tenant, [_event],
                await Rows<HumanAssignmentEventFact>(0), await Rows<HumanEventAssignmentFact>(1),
                await Rows<HumanAssignmentRoleFact>(2), await Rows<HumanAssignmentPermissionFact>(3),
                await Rows<HumanAssignmentActorFact>(4), await Rows<HumanAssignmentTenantMemberFact>(5),
                await Rows<HumanAssignmentOwnerMemberFact>(6), await Rows<HumanAssignmentOwnerMemberFact>(7),
                await Rows<HumanAssignmentProvenanceFact>(8));
            requireSelection();
            return facts;
        }
        finally { native?.Dispose(); }
    }

    private async Task<ImmutableArray<T>> ReadRowsAsync<T>(string sql, DbTransaction? owner, Guid tenant, CancellationToken token)
    {
        await using var command = Command(owner);
        command.CommandText = sql;
        Parameter(command, "actor", _human);
        Parameter(command, "tenant", tenant);
        Parameter(command, "event0", _event);
        await using var reader = await command.ExecuteReaderAsync(token);
        var result = ImmutableArray.CreateBuilder<T>();
        while (await reader.ReadAsync(token))
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (int index = 0; index < reader.FieldCount; index++)
                row.Add(reader.GetName(index), await reader.IsDBNullAsync(index, token) ? null : reader.GetValue(index));
            foreach (var property in typeof(T).GetProperties())
            {
                if (!row.TryGetValue(property.Name, out object? value) || value is null) continue;
                if (property.PropertyType == typeof(bool)) row[property.Name] = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                if (property.PropertyType == typeof(Guid) || property.PropertyType == typeof(Guid?))
                    row[property.Name] = value is Guid id ? id : Guid.Parse((string)value);
                if (property.PropertyType == typeof(DateTime) || property.PropertyType == typeof(DateTime?))
                    row[property.Name] = DateTime.SpecifyKind(value is DateTime date ? date
                        : DateTime.Parse((string)value, CultureInfo.InvariantCulture), DateTimeKind.Utc);
            }
            result.Add(JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(row), Json)
                ?? throw new InvalidOperationException("ai_moderation_target_invalid_source_row"));
        }
        return result.ToImmutable();
    }

    internal async Task LockPortableAsync(DbTransaction owner, CancellationToken token)
    {
        var names = new HumanPrincipalSourceSql(_context);
        if (_context.Database.IsSqlite())
        {
            await using var command = Command(owner);
            string id = names.Column(typeof(Explore.Domain.Event), "Id");
            command.CommandText = $"UPDATE {names.Table(typeof(Explore.Domain.Event))} SET {id} = {id} WHERE 0";
            await command.ExecuteNonQueryAsync(token);
            return;
        }
        bool sqlServer = _context.Database.IsSqlServer();
        bool mysql = _context.Database.ProviderName == RelationalNamedLock.MySqlProvider;
        if (!sqlServer && !mysql) throw new NotSupportedException("ai_moderation_target_provider_not_supported");
        foreach (var source in HumanEventAssignmentSourceSql.Sources)
        {
            await using var command = Command(owner);
            if (mysql)
            {
                command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE()"
                    + " AND table_name = @table AND engine = 'InnoDB' AND table_type = 'BASE TABLE'";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "table";
                parameter.Value = _context.Model.FindEntityType(source.Type)!.GetTableName()!;
                command.Parameters.Add(parameter);
                if (Convert.ToInt32(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) != 1)
                    throw new InvalidOperationException("ai_moderation_target_requires_innodb_sources");
                command.Parameters.Clear();
            }
            command.CommandText = sqlServer ? $"SELECT 1 FROM {names.Table(source.Type)} WITH (TABLOCKX,HOLDLOCK)"
                : $"SELECT 1 FROM {names.Table(source.Type)} ORDER BY {names.Column(source.Type, source.Columns[0])} FOR UPDATE";
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) { }
        }
    }

}
