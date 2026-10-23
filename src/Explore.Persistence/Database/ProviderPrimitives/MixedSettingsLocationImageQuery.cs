using System.Text.Json.Nodes;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class MixedSettingsLocationImageQuery
{
    internal static Task<Dictionary<Guid, EventLocation>> LoadAsync(
        ExploreDbContext context, JsonArray rows, CancellationToken token)
    {
        var sql = new HumanPrincipalSourceSql(context);
        var properties = context.Model.FindEntityType(typeof(EventLocation))!.GetProperties().ToArray();
        string query = $"SELECT {string.Join(",", properties.Select(property => $"r.{sql.Quote(property.Name)} AS {sql.Column(typeof(EventLocation), property.Name)}"))}"
            + $" FROM pg_catalog.jsonb_to_recordset(@rows) AS r({string.Join(",", properties.Select(property => $"{sql.Quote(property.Name)} {property.GetRelationalTypeMapping().StoreType}"))})";
        return context.Set<EventLocation>().FromSqlRaw(query,
                new NpgsqlParameter("rows", NpgsqlDbType.Jsonb) { Value = rows.ToJsonString() })
            .IgnoreAllFilters(TenantFilterBypassReasons.DiscoveryDisclosureMutation)
            .ToDictionaryAsync(row => row.Id, token);
    }
}
