using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Exceptions;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class EventResourceProviderSqliteWriter
{
    internal static async Task WriteAsync(
        ExploreDbContext context, RelationalConnectionIdentity connection, DbTransaction? transaction,
        Type type, JsonObject? old, JsonObject after, CancellationToken token)
    {
        var sql = new HumanPrincipalSourceSql(context);
        await using var command = connection.Connection.CreateCommand();
        command.Transaction = transaction;
        var properties = context.Model.FindEntityType(type)!.GetProperties().ToArray();
        var columns = new List<string>();
        var values = new List<string>();
        foreach (var property in properties)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "p" + values.Count;
            object? value = after[property.Name]?.Deserialize(property.ClrType);
            if (value is not null && property.GetRelationalTypeMapping().Converter is { } converter)
                value = converter.ConvertToProvider(value);
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
            columns.Add(sql.Column(type, property.Name));
            values.Add("@" + parameter.ParameterName);
        }
        int id = Array.FindIndex(properties, property => property.Name == "Id");
        command.CommandText = old is null
            ? $"INSERT INTO {sql.Table(type)} ({string.Join(",", columns)}) VALUES ({string.Join(",", values)})"
            : $"UPDATE {sql.Table(type)} SET {string.Join(",", columns.Zip(values, (column, value) => column + "=" + value))}"
                + $" WHERE {columns[id]} = {values[id]}";
        if (await command.ExecuteNonQueryAsync(token) != 1)
            throw new ConcurrencyConflictException(ConcurrencyConflictException.ConcurrentUpdate,
                "The resource provider binding or activation changed; reload and retry.");
    }
}
