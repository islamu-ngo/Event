using System.Data.Common;
using System.Text.Json.Nodes;

namespace Explore.Persistence.Security;

internal static partial class ManagedProviderProvisioningDataModel
{
    internal static async Task<JsonArray> ReadRowsAsync(ExploreDbContext context, Relation relation,
        DbCommand command, CancellationToken token)
    {
        var entity = context.Model.FindEntityType(relation.Type)!;
        var rows = new JsonArray();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var row = new JsonObject();
            for (int index = 0; index < relation.Columns.Length; index++)
                row[relation.Columns[index]] = Value(entity.FindProperty(relation.Columns[index])!, reader.GetValue(index));
            rows.Add(row);
        }
        return rows;
    }
}
