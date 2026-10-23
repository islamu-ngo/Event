using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Tracker-free provider source SQL using the caller's original observed and created row sets.</summary>
internal sealed class HumanEventProviderSourceQueries(
    ExploreDbContext _context, RelationalConnectionIdentity connection, HumanCommitSubject _subject,
    HumanEventProviderSourceTarget _target, PostgresNativeQueueEnrollment? _enrollment)
{
    private readonly DbConnection _connection = connection.Connection;

    private DbCommand Command(DbTransaction? transaction)
    {
        var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = _context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        return command;
    }

    private void Parameters(DbCommand command)
    {
        void Add(string name, object? value, DbType type = DbType.Guid)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.DbType = type;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        Add("tenant", _target.TenantId);
        Add("event", _target.EventId);
        Add("binding", _target.BindingId);
        Add("connection", _target.ConnectionId);
        Add("effect", _target.EffectId);
        Add("submission", _target.SubmissionId);
        Add("form", _target.FormId);
        Add("version", _target.FormVersionId);
        Add("secret", _target.BindingRequest?.WebhookSecretBindingId);
        if (_context.Database.IsNpgsql())
        {
            Add("actor", _subject.PrincipalId);
            Add("operation", (int)_target.Operation, DbType.Int32);
        }
    }

    internal async Task<HumanEventProviderSourceFacts> ReadAsync(
        DbTransaction? transaction, CancellationToken ct, bool final, HumanEventProviderSourceFacts? observed,
        Func<ImmutableArray<(HumanEventProviderSource Source, Guid Id)>> createdRows, Action requireSelection)
    {
        var sql = new HumanEventProviderSourceSql(_context, transaction is not null && !_context.Database.IsNpgsql());
        var rows = ImmutableArray.CreateBuilder<HumanEventProviderSourceRow>();
        var created = createdRows();
        if (_context.Database.IsNpgsql())
        {
            await using var command = Command(transaction);
            command.CommandText = PostgresHumanEventProviderSourceContract.AdmissionSql(_context, _enrollment!);
            await command.ExecuteNonQueryAsync(ct);
            command.CommandText = $"SELECT {(transaction is null
                ? PostgresHumanEventProviderSourceContract.ObserveFunctionSql(_context)
                : final ? PostgresHumanEventProviderSourceContract.ValidateFunctionSql(_context)
                : PostgresHumanEventProviderSourceContract.AcquireFunctionSql(_context))}"
                + "(@actor,@tenant,@event,@operation,@binding,@connection,@effect,@submission,@form,@version,@secret)";
            Parameters(command);
            using var document = JsonDocument.Parse((string)(await command.ExecuteScalarAsync(ct)
                ?? throw new InvalidOperationException("human_event_provider_native_facts_missing")));
            foreach (var source in HumanEventProviderSourceSql.Sources)
            {
                foreach (var row in document.RootElement.GetProperty(((int)source.Source).ToString(CultureInfo.InvariantCulture)).EnumerateArray())
                {
                    var values = ImmutableSortedDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
                    foreach (var property in sql.Properties(source.Type))
                    {
                        var element = row.GetProperty(property.Name);
                        string? value = element.ValueKind switch
                        {
                            JsonValueKind.Null => null,
                            JsonValueKind.True => "true",
                            JsonValueKind.False => "false",
                            JsonValueKind.String => element.GetString(),
                            _ => element.GetRawText()
                        };
                        if (property.ClrType == typeof(byte[]) && value is not null)
                            value = value.StartsWith("\\x", StringComparison.Ordinal) ? value[2..] : value;
                        values.Add(property.Name, value);
                    }
                    rows.Add(Row(source.Source, values.ToImmutable()));
                }
            }
            command.Parameters.Clear();
            command.CommandText = PostgresHumanEventProviderSourceContract.AdmissionSql(_context, _enrollment!);
            await command.ExecuteNonQueryAsync(ct);
        }
        else
        {
            foreach (var source in HumanEventProviderSourceSql.Sources)
            {
                IEnumerable<Guid[]> batches = final
                    ? observed!.Of(source.Source).Select(row => row.Id)
                        .Concat(created.Where(row => row.Source == source.Source).Select(row => row.Id))
                        .Distinct().Chunk(500).Prepend([])
                    : [Array.Empty<Guid>()];
                foreach (Guid[] originals in batches)
                {
                    await using var command = Command(transaction);
                    command.CommandText = originals.Length == 0 ? sql.Query(source.Source, source.Type)
                        : sql.Query(source.Source, source.Type,
                            $"{sql.C(source.Type, "s", "Id")} IN ({string.Join(",", originals.Select((_, index) => $"@original{index}"))})",
                            originalsOnly: true);
                    Parameters(command);
                    for (int index = 0; index < originals.Length; index++)
                    {
                        var parameter = command.CreateParameter();
                        parameter.ParameterName = $"original{index}";
                        parameter.DbType = DbType.Guid;
                        parameter.Value = originals[index];
                        command.Parameters.Add(parameter);
                    }
                    await using var reader = await command.ExecuteReaderAsync(ct);
                    var properties = sql.Properties(source.Type);
                    while (await reader.ReadAsync(ct))
                    {
                        var values = ImmutableSortedDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
                        for (int index = 0; index < reader.FieldCount; index++)
                        {
                            object? value = await reader.IsDBNullAsync(index, ct) ? null : reader.GetValue(index);
                            Type type = Nullable.GetUnderlyingType(properties[index].ClrType) ?? properties[index].ClrType;
                            values.Add(reader.GetName(index), value switch
                            {
                                null => null,
                                _ when type == typeof(bool) => Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? "true" : "false",
                                Guid guid => guid.ToString("D"),
                                byte[] bytes => Convert.ToHexStringLower(bytes),
                                DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
                                DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
                                _ => Convert.ToString(value, CultureInfo.InvariantCulture)
                            });
                        }
                        rows.Add(Row(source.Source, values.ToImmutable()));
                    }
                }
            }
        }
        requireSelection();
        if (final && created.Any(createdRow => !rows.Any(row => row.Source == createdRow.Source && row.Id == createdRow.Id)))
            throw new InvalidOperationException("human_event_provider_created_row_left_original_source");
        var distinct = rows.GroupBy(row => (row.Source, row.Id)).Select(group =>
        {
            var first = group.First();
            if (group.Any(row => !row.Values.SequenceEqual(first.Values)))
                throw new InvalidOperationException("human_event_provider_original_row_changed_during_read");
            return first;
        });
        return new(_subject.PrincipalId, _target.TenantId, _target,
            distinct.OrderBy(row => row.Source).ThenBy(row => row.Id).ToImmutableArray());
    }

    private static HumanEventProviderSourceRow Row(HumanEventProviderSource source, ImmutableSortedDictionary<string, string?> values) =>
        new(source, Guid.Parse(values["Id"]!), Guid.Parse(values[source == HumanEventProviderSource.SecretBinding ? "ScopeId" : "TenantId"]!), values);

    internal async Task RequirePortableAsync(DbTransaction transaction, CancellationToken ct)
    {
        var sql = new HumanEventProviderSourceSql(_context);
        bool sqlite = _context.Database.IsSqlite();
        bool sqlServer = _context.Database.IsSqlServer();
        bool mysql = _context.Database.ProviderName == RelationalNamedLock.MySqlProvider;
        if (!sqlite && !sqlServer && !mysql)
            throw new NotSupportedException("human_event_provider_source_provider_not_supported");
        if (sqlite)
        {
            await using var command = Command(transaction);
            string id = sql.Names.Column(typeof(RegistrationForm), "Id");
            command.CommandText = $"UPDATE {sql.Names.Table(typeof(RegistrationForm))} SET {id} = {id} WHERE 0";
            await command.ExecuteNonQueryAsync(ct);
        }
        foreach (var source in HumanEventProviderSourceSql.Sources)
        {
            await using var command = Command(transaction);
            command.CommandText = sqlite
                ? "SELECT COUNT(*) FROM sqlite_schema WHERE name = @table AND type = 'table'"
                : sqlServer
                    ? "SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id "
                        + "WHERE t.name = @table AND s.name = @schema AND t.is_memory_optimized = 0 AND t.is_filetable = 0"
                    : "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() "
                        + "AND table_name = @table AND engine = 'InnoDB' AND table_type = 'BASE TABLE'";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "table";
            parameter.Value = _context.Model.FindEntityType(source.Type)!.GetTableName()!;
            command.Parameters.Add(parameter);
            if (sqlServer)
            {
                var schema = command.CreateParameter();
                schema.ParameterName = "schema";
                schema.Value = _context.Model.FindEntityType(source.Type)!.GetSchema() ?? "dbo";
                command.Parameters.Add(schema);
            }
            if (Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) != 1)
                throw new InvalidOperationException("human_event_provider_requires_physical_source_tables");
        }
    }

}
