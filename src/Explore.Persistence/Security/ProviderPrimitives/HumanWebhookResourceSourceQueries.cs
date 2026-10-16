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

/// <summary>Fixed webhook source projections and locks on the captured original connection.</summary>
internal sealed class HumanWebhookResourceSourceQueries(
    ExploreDbContext _context, RelationalConnectionIdentity connection, Guid? _tenant,
    HumanCommitSubject _subject, HumanWebhookResourceTarget _target,
    PolicySourceSetPurpose _purpose, PostgresNativeQueueEnrollment? _enrollment)
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
        void Add(string name, object? value, DbType type)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.DbType = type;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        Add("actor", _subject.PrincipalId, DbType.Guid);
        Add("tenant", _tenant, DbType.Guid);
        Add("operation", (int)_target.Operation, DbType.Int32);
        Add("id", _target.Id, DbType.Guid);
        Add("kind", _target.OwnerKindId, DbType.Int32);
        Add("owner", _target.RequestedOwnerId, DbType.Guid);
        Add("consumer", _target.ConsumerFilterId, DbType.Guid);
        Add("endpoint", _target.EndpointFilterId, DbType.Guid);
        if (_context.Database.IsNpgsql()) Add("purpose", (int)_purpose, DbType.Int32);
    }

    internal async Task<HumanWebhookResourceSourceFacts> ReadAsync(
        DbTransaction? transaction, Action requireSelection, CancellationToken ct)
    {
        JsonDocument? native = null;
        try
        {
            if (_context.Database.IsNpgsql())
            {
                await using var command = Command(transaction);
                command.CommandText = PostgresHumanWebhookResourceSourceContract.AdmissionSql(_context, _enrollment!);
                await command.ExecuteNonQueryAsync(ct);
                command.CommandText = $"SELECT {(transaction is null
                    ? PostgresHumanWebhookResourceSourceContract.ObserveFunctionSql(_context)
                    : PostgresHumanWebhookResourceSourceContract.AcquireFunctionSql(_context))}"
                    + "(@actor,@purpose,@tenant,@operation,@id,@kind,@owner,@consumer,@endpoint)";
                Parameters(command);
                native = JsonDocument.Parse((string)(await command.ExecuteScalarAsync(ct)
                    ?? throw new InvalidOperationException("human_webhook_native_facts_missing")));
                command.Parameters.Clear();
                command.CommandText = PostgresHumanWebhookResourceSourceContract.AdmissionSql(_context, _enrollment!);
                await command.ExecuteNonQueryAsync(ct);
            }
            bool current = transaction is not null && _context.Database.ProviderName == RelationalNamedLock.MySqlProvider;
            string[] queries = native is null ? new HumanWebhookResourceSourceSql(_context, current).Queries : [];
            async Task<ImmutableArray<T>> Rows<T>(int index) => native is null
                ? await RowsAsync<T>(queries[index], transaction, ct)
                : native.RootElement.GetProperty($"q{index}").Deserialize<ImmutableArray<T>>(Json);
            var facts = new HumanWebhookResourceSourceFacts(_subject.PrincipalId, _tenant, _target,
                await Rows<HumanWebhookConsumerFact>(0), await Rows<HumanWebhookEndpointFact>(1),
                await Rows<HumanWebhookAttemptFact>(2), await Rows<HumanWebhookMessageFact>(3),
                await Rows<HumanWebhookLocalTargetFact>(4), await Rows<HumanWebhookPlanFact>(5),
                await Rows<HumanWebhookPublicationFact>(6), await Rows<HumanWebhookBulkReplayFact>(7),
                await Rows<HumanWebhookBindingFact>(8), await Rows<HumanWebhookInstanceFact>(9),
                await Rows<HumanWebhookOwnerFact>(10), await Rows<HumanWebhookPlacementFact>(11),
                await Rows<HumanWebhookOwnerFact>(12), await Rows<HumanWebhookPlacementFact>(13),
                await Rows<HumanWebhookTenantUserFact>(14));
            requireSelection();
            return facts;
        }
        finally { native?.Dispose(); }
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
                var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (type == typeof(Guid)) values[property.Name] = value is Guid guid ? guid : Guid.Parse((string)value);
                if (type == typeof(bool)) values[property.Name] = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                if (type == typeof(DateTime))
                    values[property.Name] = value is DateTime date ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
                        : DateTime.Parse((string)value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                if (type == typeof(DateTimeOffset))
                    values[property.Name] = value switch
                    {
                        DateTimeOffset offset => offset,
                        DateTime date => new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)),
                        string text => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
                        _ => throw new InvalidOperationException("human_webhook_time_storage_invalid")
                    };
            }
            rows.Add(JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(values), Json)
                ?? throw new InvalidOperationException("human_webhook_fact_row_invalid"));
        }
        return rows.ToImmutable();
    }

    internal async Task LockPortableAsync(DbTransaction transaction, CancellationToken ct)
    {
        var names = new HumanPrincipalSourceSql(_context);
        if (_context.Database.IsSqlite())
        {
            await using var command = Command(transaction);
            string id = names.Column(typeof(WebhookConsumer), "Id");
            command.CommandText = $"UPDATE {names.Table(typeof(WebhookConsumer))} SET {id} = {id} WHERE 0";
            await command.ExecuteNonQueryAsync(ct);
            return;
        }
        bool sqlServer = _context.Database.IsSqlServer();
        bool mysql = _context.Database.ProviderName == RelationalNamedLock.MySqlProvider;
        if (!sqlServer && !mysql) throw new NotSupportedException("human_webhook_provider_not_supported");
        foreach (var source in HumanWebhookResourceSourceSql.Sources)
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
                    throw new InvalidOperationException("human_webhook_requires_innodb_sources");
                command.Parameters.Clear();
            }
            command.CommandText = sqlServer
                ? $"SELECT 1 FROM {names.Table(source.Type)} WITH (TABLOCKX,HOLDLOCK)"
                : $"SELECT 1 FROM {names.Table(source.Type)} ORDER BY {names.Column(source.Type, source.Columns[0])} FOR UPDATE";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) { }
        }
    }

}
