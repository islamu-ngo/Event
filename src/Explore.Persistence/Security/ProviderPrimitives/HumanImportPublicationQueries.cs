using System.Collections.Immutable;
using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using static Explore.Persistence.Security.HumanConfigurationImportPublicationStore;

namespace Explore.Persistence.Security;

/// <summary>Fixed publication row reads and preimage-conditional writes on the retained connector.</summary>
internal sealed class HumanImportPublicationQueries(
    ExploreDbContext context, RelationalConnectionIdentity connection, DbTransaction transaction)
{
    internal static DateTime StorageTimestamp(ExploreDbContext context, DateTime occurredAt) =>
        context.Database.IsNpgsql()
            ? new DateTime(occurredAt.Ticks - occurredAt.Ticks % 10, DateTimeKind.Utc) : occurredAt;

    internal async Task<Image> ReadAsync(ConfigurationImportScope scope, Guid? tenant,
        PostgresNativeQueueEnrollment? enrollment, Action requireSelection, CancellationToken token)
    {
        if (context.Database.IsNpgsql() && scope == ConfigurationImportScope.Instance)
            return await PostgresConfigurationImportPublicationPolicyContract.ReadOriginalAsync(context,
                enrollment ?? throw new UnauthorizedAccessException("Original import enrollment is required."), token);
        var systems = ImmutableArray.CreateBuilder<SystemRow>();
        await using (var command = ReadCommand<SystemSetting>(SystemProperties, null))
        await using (var rows = await command.ExecuteReaderAsync(token))
            while (await rows.ReadAsync(token))
                systems.Add(new(rows.GetGuid(0), rows.GetString(1), rows.GetString(2), rows.GetBoolean(3),
                    rows.GetInt32(4), Text(rows, 5), Text(rows, 6), Text(rows, 7), rows.GetInt32(8),
                    rows.GetDateTime(9).Ticks, Id(rows, 10), Ticks(rows, 11), Id(rows, 12)));
        var tenants = ImmutableArray.CreateBuilder<TenantRow>();
        await using (var command = ReadCommand<TenantSetting>(TenantProperties, tenant))
        await using (var rows = await command.ExecuteReaderAsync(token))
            while (await rows.ReadAsync(token))
                tenants.Add(new(rows.GetGuid(0), rows.GetGuid(1), rows.GetString(2), rows.GetString(3),
                    rows.GetBoolean(4), rows.GetDateTime(5).Ticks, Id(rows, 6), Ticks(rows, 7), Id(rows, 8)));
        requireSelection();
        return new(systems.OrderBy(row => row.Key, StringComparer.Ordinal).ToImmutableArray(),
            tenants.OrderBy(row => row.Tenant).ThenBy(row => row.Key, StringComparer.Ordinal).ToImmutableArray());
    }

    private DbCommand ReadCommand<T>(string[] properties, Guid? tenant)
    {
        var command = connection.Connection.CreateCommand();
        command.Transaction = transaction;
        string columns = string.Join(", ", properties.Select(Column<T>));
        var keys = PublicationPolicySettingKeys.All.Select((key, index) => Parameter(command, "key" + index, key));
        command.CommandText = $"SELECT {columns} FROM {Table<T>()} WHERE {Column<T>("SettingKey")} IN ({string.Join(", ", keys)})";
        if (tenant.HasValue)
            command.CommandText += $" AND {Column<T>("TenantId")} = {Parameter(command, "tenant", tenant.Value)}";
        return command;
    }

    internal async Task WriteAsync(ConfigurationImportScope scope, Guid? tenant,
        PostgresNativeQueueEnrollment? enrollment, HumanConfigurationImportPublication publication,
        Image beforeImage, Image afterImage, CancellationToken token)
    {
        if (context.Database.IsNpgsql() && scope == ConfigurationImportScope.Instance)
        {
            await PostgresConfigurationImportPublicationPolicyContract.WriteOriginalAsync(context,
                enrollment!, tenant, publication, beforeImage, afterImage, token);
            return;
        }
        foreach (var mutation in publication.Mutations.OrderBy(item => PublicationPolicySettingKeys.All.ToList().IndexOf(item.Key)))
        {
            if (publication.TenantId is { } selected)
            {
                var before = beforeImage.Tenants.SingleOrDefault(row => row.Tenant == selected && row.Key == mutation.Key);
                var after = afterImage.Tenants.Single(row => row.Tenant == selected && row.Key == mutation.Key);
                if (before == after) continue;
                await WriteRowAsync<TenantSetting>(TenantProperties, before is null ? null : Values(before), Values(after), token);
            }
            else
            {
                var before = beforeImage.Systems.SingleOrDefault(row => row.Key == mutation.Key);
                var after = afterImage.Systems.Single(row => row.Key == mutation.Key);
                if (before == after) continue;
                await WriteRowAsync<SystemSetting>(SystemProperties, before is null ? null : Values(before), Values(after), token);
            }
        }
    }

    private async Task WriteRowAsync<T>(string[] properties, object?[]? before, object?[] after, CancellationToken token)
    {
        await using var command = connection.Connection.CreateCommand();
        command.Transaction = transaction;
        string[] parameters = after.Select((value, index) => Parameter(command, "after" + index, value)).ToArray();
        if (before is null)
            command.CommandText = $"INSERT INTO {Table<T>()} ({string.Join(", ", properties.Select(Column<T>))}) " +
                $"VALUES ({string.Join(", ", parameters)}) ON CONFLICT DO NOTHING";
        else
        {
            string equal = context.Database.IsNpgsql() ? "IS NOT DISTINCT FROM" : "IS";
            command.CommandText = $"UPDATE {Table<T>()} SET " +
                string.Join(", ", properties.Select((property, index) => $"{Column<T>(property)} = {parameters[index]}")) +
                " WHERE " + string.Join(" AND ", properties.Select((property, index) =>
                    $"{Column<T>(property)} {equal} {Parameter(command, "before" + index, before[index])}"));
        }
        if (await command.ExecuteNonQueryAsync(token) != 1)
            throw new UnauthorizedAccessException("A retained publication preimage or absence changed.");
    }

    private string Table<T>()
    {
        var entity = context.Model.FindEntityType(typeof(T))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
    }

    private string Column<T>(string property)
    {
        var entity = context.Model.FindEntityType(typeof(T))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.FindProperty(property)!
            .GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
    }

    private static string Parameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
        return "@" + name;
    }

    private static string? Text(DbDataReader row, int index) => row.IsDBNull(index) ? null : row.GetString(index);
    private static Guid? Id(DbDataReader row, int index) => row.IsDBNull(index) ? null : row.GetGuid(index);
    private static long? Ticks(DbDataReader row, int index) => row.IsDBNull(index) ? null : row.GetDateTime(index).Ticks;
    private static DateTime? Date(long? ticks) => ticks is null ? null : new DateTime(ticks.Value, DateTimeKind.Utc);
    private static object?[] Values(SystemRow row) => [row.Id, row.Key, row.Value, row.Locked, row.Type, row.Allowed,
        row.Description, row.Category, row.Order, Date(row.Created), row.Creator, Date(row.Updated), row.Updater];
    private static object?[] Values(TenantRow row) => [row.Id, row.Tenant, row.Key, row.Value, row.Locked,
        Date(row.Created), row.Creator, Date(row.Updated), row.Updater];
    private static readonly string[] SystemProperties = ["Id", "SettingKey", "Value", "IsLocked", "SettingValueTypeId",
        "AllowedValues", "Description", "Category", "DisplayOrder", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"];
    private static readonly string[] TenantProperties = ["Id", "TenantId", "SettingKey", "Value", "IsLocked",
        "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"];
}
