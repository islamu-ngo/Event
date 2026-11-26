using System.Data.Common;
using Explore.Domain;
using Explore.Domain.Settings.Documents;
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
/// Retains fixed guidance/workspace source predicates on the caller's original transaction.
/// These are storage exclusions, not settings write grants or learning workflow authority.
/// </summary>
internal sealed class LearningSourceProviderLock(
    ExploreDbContext context, RelationalConnectionIdentity connection, Guid tenant)
{
    internal async Task AcquireAsync(DbTransaction transaction, CancellationToken token)
    {
        await using var command = connection.Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        if (context.Database.IsNpgsql())
        {
            var names = new HumanPrincipalSourceSql(context);
            command.CommandText = $"""
                SELECT {names.Name("ordinary_settings_learning_retain")}(@tenant);
                SELECT {names.Name("mixed_settings_learning_retain")}(@tenant);
                """;
            command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = tenant });
            await command.ExecuteNonQueryAsync(token);
            return;
        }
        if (context.Database.IsSqlite())
        {
            string id = Column<SystemSetting>(nameof(SystemSetting.Id));
            command.CommandText = $"UPDATE {Table<SystemSetting>()} SET {id} = {id} WHERE 0";
            await command.ExecuteNonQueryAsync(token);
            return;
        }
        bool sqlServer = context.Database.IsSqlServer();
        bool mysql = context.Database.ProviderName == RelationalNamedLock.MySqlProvider;
        if (!sqlServer && !mysql)
            throw new NotSupportedException("learning_source_provider_not_supported");
        if (mysql)
        {
            command.CommandText = """
                SELECT COUNT(*) FROM information_schema.tables
                WHERE table_schema = DATABASE() AND table_name IN (@system, @tenant, @document)
                    AND engine = 'InnoDB' AND table_type = 'BASE TABLE'
                """;
            Add("system", context.Model.FindEntityType(typeof(SystemSetting))!.GetTableName()!);
            Add("tenant", context.Model.FindEntityType(typeof(TenantSetting))!.GetTableName()!);
            Add("document", context.Model.FindEntityType(typeof(TenantSettingsDocument))!.GetTableName()!);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(token),
                    System.Globalization.CultureInfo.InvariantCulture) != 3)
                throw new InvalidOperationException("learning_sources_require_innodb");
            command.Parameters.Clear();
        }
        for (int index = 0; index < LearningSourceRetention.Keys.Count; index++)
            Add($"key{index}", LearningSourceRetention.Keys[index]);
        Add("tenant", tenant);
        Add("document", LearningSourceRetention.GuidanceDocumentKey);
        string keys = string.Join(",", LearningSourceRetention.Keys.Select((_, index) => $"@key{index}"));
        string hint = sqlServer ? " WITH (UPDLOCK, HOLDLOCK)" : "";
        string suffix = mysql ? " FOR UPDATE" : "";
        command.CommandText = $"SELECT 1 FROM {Table<SystemSetting>()}{hint}"
            + $" WHERE {Column<SystemSetting>(nameof(SystemSetting.SettingKey))} IN ({keys})"
            + $" ORDER BY {Column<SystemSetting>(nameof(SystemSetting.SettingKey))}{suffix}";
        await DrainAsync();
        command.CommandText = $"SELECT 1 FROM {Table<TenantSetting>()}{hint}"
            + $" WHERE {Column<TenantSetting>(nameof(TenantSetting.TenantId))} = @tenant"
            + $" AND {Column<TenantSetting>(nameof(TenantSetting.SettingKey))} IN ({keys})"
            + $" ORDER BY {Column<TenantSetting>(nameof(TenantSetting.SettingKey))}{suffix}";
        await DrainAsync();
        command.CommandText = $"SELECT 1 FROM {Table<TenantSettingsDocument>()}{hint}"
            + $" WHERE {Column<TenantSettingsDocument>(nameof(TenantSettingsDocument.TenantId))} = @tenant"
            + $" AND {Column<TenantSettingsDocument>(nameof(TenantSettingsDocument.DocumentKey))} = @document{suffix}";
        await DrainAsync();

        void Add(string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
        async Task DrainAsync()
        {
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) { }
        }
    }

    private string Table<T>()
    {
        var entity = context.Model.FindEntityType(typeof(T))!;
        return context.GetService<ISqlGenerationHelper>()
            .DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
    }

    private string Column<T>(string property)
    {
        var entity = context.Model.FindEntityType(typeof(T))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.FindProperty(property)!
            .GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
    }
}
