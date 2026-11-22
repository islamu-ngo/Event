using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Persists one canonical row without flushing unrelated tracked state. PostgreSQL's
/// Runtime cannot use EF table DML for this cross-scope snapshot; its fixed storage
/// port supplies only that missing privilege. Other supported providers use their
/// EF parameter mappings on the retained transaction. The fixed row port avoids
/// flushing unrelated tracked changes while inserting or conditionally replacing
/// the independently compiled canonical image.
/// </summary>
internal static class VisitorSettingsStorage
{
    internal static async Task WriteAsync(ExploreDbContext context, Guid? tenant, Type type,
        JsonObject? before, JsonObject? after, Action requireOwner, CancellationToken token)
    {
        requireOwner();
        if (type != typeof(SystemSetting) && type != typeof(TenantSetting))
            throw new UnauthorizedAccessException("Visitor storage accepts only fixed setting row types.");
        if (context.Database.IsNpgsql())
        {
            await PostgresVisitorSettingsStorage.WriteAsync(context, tenant, type, before, after, requireOwner, token);
            return;
        }
        _ = RelationalProviderClassifier.Classify(context.Database);
        var entity = context.Model.FindEntityType(type)!;
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var sql = context.GetService<ISqlGenerationHelper>();
        var properties = entity.GetProperties().ToArray();
        string Column(int index) => sql.DelimitIdentifier(properties[index].GetColumnName(table)!);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        for (int index = 0; index < properties.Length; index++)
        {
            foreach (var (prefix, image) in new[] { ("old", before), ("next", after) })
            {
                if (image is null) continue;
                var property = properties[index];
                var parameter = property.GetRelationalTypeMapping().CreateParameter(command,
                    $"{prefix}{index}", image[property.Name]?.Deserialize(property.ClrType), property.IsNullable);
                command.Parameters.Add(parameter);
            }
        }
        string relation = sql.DelimitIdentifier(table.Name, table.Schema);
        string predicate = string.Join(" AND ", Enumerable.Range(0, properties.Length)
            .Select(index => $"({Column(index)} = @old{index} OR ({Column(index)} IS NULL AND @old{index} IS NULL))"));
        command.CommandText = before is null
            ? $"INSERT INTO {relation} ({string.Join(",", Enumerable.Range(0, properties.Length).Select(Column))}) "
                + $"VALUES ({string.Join(",", Enumerable.Range(0, properties.Length).Select(index => $"@next{index}"))})"
            : after is null ? $"DELETE FROM {relation} WHERE {predicate}"
            : $"UPDATE {relation} SET {string.Join(",", Enumerable.Range(0, properties.Length).Select(index => $"{Column(index)}=@next{index}"))} WHERE {predicate}";
        if (await command.ExecuteNonQueryAsync(token) != 1)
            throw new UnauthorizedAccessException("The original visitor storage row changed.");
        requireOwner();
    }
}
