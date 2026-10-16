using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Executes the fixed sentinel capability or portable source locks on the captured connection.
/// </summary>
internal sealed class PolicySourceSetProviderLock(
    ExploreDbContext _context, RelationalConnectionIdentity connection, Guid? _tenant,
    PolicySourceSetPurpose _purpose, PostgresNativeQueueEnrollment? _enrollment)
{
    private readonly DbConnection _connection = connection.Connection;

    internal async Task AcquirePostgresAsync(DbTransaction transaction, CancellationToken ct)
    {
        await using var command = Command(transaction);
        command.CommandText = PostgresPolicySourceSetContract.AdmissionSql(_context, _enrollment!);
        await command.ExecuteNonQueryAsync(ct);
        command.CommandText = $"SELECT {PostgresPolicySourceSetContract.AcquireFunctionSql(_context)}(@purpose, @tenant)";
        command.Parameters.Add(new NpgsqlParameter("purpose", NpgsqlDbType.Integer) { Value = (int)_purpose });
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = (object?)_tenant ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(ct);
        command.Parameters.Clear();
        command.CommandText = PostgresPolicySourceSetContract.AdmissionSql(_context, _enrollment!);
        await command.ExecuteNonQueryAsync(ct);
    }

    internal async Task AcquirePortableAsync(DbTransaction transaction, CancellationToken ct)
    {
        await using var command = Command(transaction);
        string permissions = Table<RolePermission>();
        if (_context.Database.IsSqlite())
        {
            // Even an empty UPDATE starts SQLite's native write transaction. A WAL snapshot
            // that can no longer upgrade fails SQLITE_BUSY_SNAPSHOT rather than retaining old facts.
            string role = Column<RolePermission>(nameof(RolePermission.RoleId));
            command.CommandText = $"UPDATE {permissions} SET {role} = {role} WHERE 0";
            await command.ExecuteNonQueryAsync(ct);
            return;
        }
        bool sqlServer = _context.Database.IsSqlServer();
        bool mysql = _context.Database.ProviderName == RelationalNamedLock.MySqlProvider;
        if (!sqlServer && !mysql)
            throw new NotSupportedException("policy_source_set_provider_not_supported");
        if (mysql)
        {
            // Next-key exclusion requires transactional InnoDB, not merely an isolation label.
            command.CommandText = """
                SELECT COUNT(*) FROM information_schema.tables
                WHERE table_schema = DATABASE() AND table_name IN (@roles, @system, @tenant)
                    AND engine = 'InnoDB' AND table_type = 'BASE TABLE'
                """;
            foreach (var (name, type) in new[]
            {
                ("roles", typeof(RolePermission)), ("system", typeof(SystemSetting)), ("tenant", typeof(TenantSetting))
            })
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = _context.Model.FindEntityType(type)!.GetTableName()!;
                command.Parameters.Add(parameter);
            }
            if (Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) != 3)
                throw new InvalidOperationException("policy_source_set_requires_innodb_sources");
            command.Parameters.Clear();
        }
        // Fixed order: complete RolePermission set, instance route keys, tenant route keys.
        command.CommandText = sqlServer
            ? $"SELECT 1 FROM {permissions} WITH (TABLOCKX, HOLDLOCK)"
            : $"SELECT 1 FROM {permissions} ORDER BY {Column<RolePermission>(nameof(RolePermission.RoleId))}, "
                + $"{Column<RolePermission>(nameof(RolePermission.PermissionId))} FOR UPDATE";
        await DrainAsync(command, ct);
        string keys = string.Join(",", PolicySourceSetFence.RouteKeys.Select((_, index) => $"@key{index}"));
        for (int index = 0; index < PolicySourceSetFence.RouteKeys.Count; index++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = $"key{index}";
            parameter.Value = PolicySourceSetFence.RouteKeys[index];
            command.Parameters.Add(parameter);
        }
        foreach (Type type in new[] { typeof(SystemSetting), typeof(TenantSetting) })
        {
            var entity = _context.Model.FindEntityType(type)!;
            string table = _context.GetService<ISqlGenerationHelper>()
                .DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            string key = Column(type, nameof(SystemSetting.SettingKey));
            string order = type == typeof(TenantSetting)
                ? $"{key}, {Column<TenantSetting>(nameof(TenantSetting.TenantId))}" : key;
            command.CommandText = sqlServer
                ? $"SELECT 1 FROM {table} WITH (UPDLOCK, HOLDLOCK) WHERE {key} IN ({keys}) ORDER BY {order}"
                : $"SELECT 1 FROM {table} WHERE {key} IN ({keys}) ORDER BY {order} FOR UPDATE";
            await DrainAsync(command, ct);
        }
    }

    private static async Task DrainAsync(DbCommand command, CancellationToken ct)
    {
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) { }
    }

    private DbCommand Command(DbTransaction transaction)
    {
        var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = _context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        return command;
    }

    private string Table<T>()
    {
        var entity = _context.Model.FindEntityType(typeof(T))!;
        return _context.GetService<ISqlGenerationHelper>()
            .DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
    }

    private string Column<T>(string property) => Column(typeof(T), property);

    private string Column(Type type, string property)
    {
        var entity = _context.Model.FindEntityType(type)!;
        string name = entity.FindProperty(property)!.GetColumnName(
            StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!;
        return _context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name);
    }

}
