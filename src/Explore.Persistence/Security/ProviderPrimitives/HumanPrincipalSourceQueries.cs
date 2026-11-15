using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Reads principal facts on the caller's captured connection and transaction.
/// </summary>
internal sealed class HumanPrincipalSourceQueries(
    ExploreDbContext _context, HumanCommitSubject _subject, PolicySourceSetPurpose _purpose,
    LocalSessionAuthority? _local, PostgresNativeQueueEnrollment? _enrollment,
    RelationalConnectionIdentity connection, Guid? _tenant, LocalIdentityExecutionLease? selectedExecution = null)
{
    private readonly DbConnection _connection = connection.Connection;
    private readonly HumanPrincipalSourceSql _sql = new(_context);
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
        Add("provider", _subject.AccountKey is { } key ? (int)key.ProviderKind : null, DbType.Int32);
        Add("account", _subject.AccountKey?.Value, DbType.String);
        Add("tenant", _tenant, DbType.Guid);
        Add("stamp", _local?.SecurityStamp, DbType.String);
    }

    internal async Task<HumanPrincipalSourceFacts> ReadAsync(
        DbTransaction? transaction, Action requireSelection, CancellationToken ct)
    {
        if (selectedExecution is not null)
        {
            requireSelection();
            var facts = await IdentityWriterHumanSourceQueries.ReadPrincipalAsync(selectedExecution, _subject, _local, ct);
            requireSelection();
            return facts;
        }
        if (!_context.Database.IsNpgsql())
        {
            void RequireOriginal()
            {
                requireSelection();
                if (!ReferenceEquals(transaction, _context.Database.CurrentTransaction?.GetDbTransaction()))
                    throw new InvalidOperationException("human_principal_transaction_changed");
            }
            return await HumanPrincipalSourceProjection.ReadAsync(
                _context, _context, _subject, _local, RequireOriginal, ct);
        }
        JsonDocument? native = null;
        try
        {
            if (_context.Database.IsNpgsql())
            {
                await using var command = Command(transaction);
                command.CommandText = PostgresHumanPrincipalSourceContract.AdmissionSql(_context, _enrollment!);
                await command.ExecuteNonQueryAsync(ct);
                command.CommandText = $"SELECT {(transaction is null
                    ? PostgresHumanPrincipalSourceContract.ObserveFunctionSql(_context)
                    : PostgresHumanPrincipalSourceContract.AcquireFunctionSql(_context))}"
                    + "(@actor,@provider,@account,@purpose,@tenant,@stamp)";
                Parameters(command);
                command.Parameters.Add(new NpgsqlParameter("purpose", NpgsqlDbType.Integer) { Value = (int)_purpose });
                native = JsonDocument.Parse((string)(await command.ExecuteScalarAsync(ct)
                    ?? throw new InvalidOperationException("human_principal_native_result_missing")));
                command.Parameters.Clear();
                command.CommandText = PostgresHumanPrincipalSourceContract.AdmissionSql(_context, _enrollment!);
                await command.ExecuteNonQueryAsync(ct);
            }
            ImmutableArray<T> Rows<T>(int index) =>
                native!.RootElement.GetProperty($"q{index}").Deserialize<ImmutableArray<T>>(Json);
            var users = Rows<HumanPrincipalAccountFact>(0);
            var bindings = Rows<HumanPrincipalBindingFact>(1);
            var platform = Rows<HumanPrincipalPlatformGrantFact>(2);
            var roles = Rows<HumanPrincipalRoleFact>(3);
            var members = Rows<HumanPrincipalTenantMembershipFact>(4);
            var grants = Rows<HumanPrincipalTenantGrantFact>(5);
            var tenants = Rows<HumanPrincipalTenantFact>(6);
            var organizations = Rows<HumanPrincipalOwnerMembershipFact>(7);
            var groups = Rows<HumanPrincipalOwnerMembershipFact>(8);
            var permissions = Rows<HumanPrincipalPermissionFact>(9);
            bool mappingsExist = native!.RootElement.GetProperty("PermissionMappingsExist").GetBoolean();
            var lists = new ImmutableArray<Guid>[4];
            for (int index = 0; index < lists.Length; index++)
            {
                var rows = Rows<GuidValue>(11 + index);
                lists[index] = rows.Select(row => row.Value).ToImmutableArray();
            }
            HumanPrincipalLocalSessionFacts? local = null;
            if (_local is not null)
            {
                ImmutableArray<T> LocalRows<T>(int index) =>
                    native.RootElement.GetProperty($"l{index}").Deserialize<ImmutableArray<T>>(Json);
                var accounts = LocalRows<HumanPrincipalLocalAccountFact>(0);
                string? metadata = LocalRows<StringValue>(1).SingleOrDefault()?.Value;
                LocalCredentialStateMetadata? state;
                try { state = string.IsNullOrWhiteSpace(metadata) ? null : JsonSerializer.Deserialize<LocalCredentialStateMetadata>(metadata, Json); }
                catch (JsonException) { state = null; }
                catch (ArgumentException) { state = null; }
                var operations = LocalRows<HumanPrincipalLocalOperationFact>(2);
                var actors = LocalRows<HumanPrincipalPersonalActorFact>(3);
                string? delivery = accounts is [var account] && !account.EmailConfirmed
                    ? LocalRows<StringValue>(4).SingleOrDefault()?.Value : null;
                local = new(accounts, state, operations, actors,
                    delivery is null ? null : JsonSerializer.Deserialize<bool>(delivery));
            }
            requireSelection();
            return new(_subject.PrincipalId, _tenant, users, bindings, platform, roles, members, grants,
                tenants, organizations, groups, permissions, mappingsExist,
                lists[0], lists[1], lists[2], lists[3], local);
        }
        finally { native?.Dispose(); }
    }

    internal async Task LockPortableAsync(DbTransaction transaction, CancellationToken ct)
    {
        if (_context.Database.IsSqlite())
            return; // The required original source-set lease already owns the native write reservation.
        bool sqlServer = _context.Database.IsSqlServer();
        bool mysql = _context.Database.ProviderName == RelationalNamedLock.MySqlProvider;
        if (!sqlServer && !mysql) throw new NotSupportedException("human_principal_provider_not_supported");
        // A deliberately conservative portable lane: native table/range exclusion, not a
        // cooperative advisory key. PostgreSQL uses narrower column privileges and fixed routines.
        var sources = _local is null ? HumanPrincipalSourceSql.Sources
            : [.. HumanPrincipalSourceSql.Sources, .. HumanPrincipalSourceSql.LocalSources];
        foreach (var source in sources)
        {
            await using var command = Command(transaction);
            if (mysql)
            {
                command.CommandText = "SELECT COUNT(*) FROM information_schema.tables "
                    + "WHERE table_schema = DATABASE() AND table_name = @table AND engine = 'InnoDB' AND table_type = 'BASE TABLE'";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "table";
                parameter.Value = _context.Model.FindEntityType(source.Type)!.GetTableName()!;
                command.Parameters.Add(parameter);
                if (Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) != 1)
                    throw new InvalidOperationException("human_principal_requires_innodb_sources");
                command.Parameters.Clear();
            }
            command.CommandText = sqlServer
                ? $"SELECT 1 FROM {_sql.Table(source.Type)} WITH (TABLOCKX,HOLDLOCK)"
                : $"SELECT 1 FROM {_sql.Table(source.Type)} ORDER BY {_sql.Column(source.Type, source.Columns[0])} FOR UPDATE";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) { }
        }
    }

    private sealed record GuidValue(Guid Value);
    private sealed record StringValue(string? Value);

}
