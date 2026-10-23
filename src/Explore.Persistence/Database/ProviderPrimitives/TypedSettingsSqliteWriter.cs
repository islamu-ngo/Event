using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class TypedSettingsSqliteWriter
{
    internal static async Task WriteAsync(
        ExploreDbContext context, DbTransaction? transaction, Type type, Guid id,
        JsonObject? old, JsonObject? after, CancellationToken token)
    {
        if (!context.Database.IsSqlite())
            throw new NotSupportedException("typed_settings_native_provider_not_supported");
        if (old is not null && JsonNode.DeepEquals(old, after))
            return;
        var sql = new HumanPrincipalSourceSql(context);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction;
        void Add(string name, object? value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        Add("id", id);
        if (after is null)
            command.CommandText = $"DELETE FROM {sql.Table(type)} WHERE {sql.Column(type, "Id")} = @id";
        else
        {
            var properties = context.Model.FindEntityType(type)!.GetProperties().ToArray();
            var columns = new List<string>();
            var parameters = new List<string>();
            foreach (var property in properties)
            {
                if (property.Name == "Id") continue;
                string parameterName = "p" + parameters.Count;
                object? value = type == typeof(Explore.Domain.Settings.Documents.TenantSettingsDocument)
                    && property.Name == nameof(Explore.Domain.Settings.Documents.TenantSettingsDocument.PayloadJson)
                    || type == typeof(Explore.Domain.Modules.TenantCapability)
                    && property.Name == nameof(Explore.Domain.Modules.TenantCapability.ConfigurationJson)
                    ? after[property.Name]?.ToJsonString() : after[property.Name]?.Deserialize(property.ClrType);
                Add(parameterName, value);
                columns.Add(sql.Column(type, property.Name));
                parameters.Add("@" + parameterName);
            }
            command.CommandText = old is null
                ? $"INSERT INTO {sql.Table(type)} ({sql.Column(type, "Id")},{string.Join(",", columns)}) VALUES (@id,{string.Join(",", parameters)})"
                : $"UPDATE {sql.Table(type)} SET {string.Join(",", columns.Zip(parameters, (column, parameter) => column + "=" + parameter))} WHERE {sql.Column(type, "Id")} = @id";
        }
        int changed = await command.ExecuteNonQueryAsync(token);
        if (changed != (old is null && after is null ? 0 : 1))
            throw new UnauthorizedAccessException("The original typed row presence changed.");
    }
}
