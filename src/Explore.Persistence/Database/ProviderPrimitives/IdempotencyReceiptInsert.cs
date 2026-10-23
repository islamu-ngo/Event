using Explore.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using MySqlConnector;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class IdempotencyReceiptInsert
{
    internal static async Task InsertAsync(ExploreDbContext context, IdempotencyRecord row, CancellationToken token)
    {
        if (!context.Database.IsRelational())
        {
            context.IdempotencyRecords.Add(row);
            await context.SaveChangesAsync(token);
            return;
        }
        var entity = context.Model.FindEntityType(typeof(IdempotencyRecord))!;
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var sql = context.GetService<ISqlGenerationHelper>();
        var properties = entity.GetProperties().ToArray();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        List<object> parameters = [];
        for (int index = 0; index < properties.Length; index++)
        {
            var property = properties[index];
            parameters.Add(property.GetRelationalTypeMapping().CreateParameter(command, $"p{index}",
                property.PropertyInfo!.GetValue(row), property.IsNullable));
        }
        string insert = $"INSERT INTO {sql.DelimitIdentifier(table.Name, table.Schema)} "
            + $"({string.Join(",", properties.Select(property => sql.DelimitIdentifier(property.GetColumnName(table)!)))}) "
            + $"VALUES ({string.Join(",", properties.Select((_, index) => sql.GenerateParameterName($"p{index}")))})";
        await context.Database.ExecuteSqlRawAsync(insert, parameters, token);
    }

    internal static bool IsUniqueViolation(Exception exception) => exception switch
    {
        SqliteException sqlite => sqlite.SqliteExtendedErrorCode is 1555 or 2067,
        MySqlException mysql => mysql.Number == 1062,
        Microsoft.Data.SqlClient.SqlException sqlServer => sqlServer.Number is 2601 or 2627,
        DbUpdateException update when update.InnerException is { } inner => IsUniqueViolation(inner),
        _ => false
    };
}
