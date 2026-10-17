using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class ExternalApiKeyAuthenticationQuery
{
    internal static async Task<ExternalApiKey?> ReadAsync(
        ExploreDbContext context, string keyId, CancellationToken cancellationToken)
    {
        IQueryable<ExternalApiKey> keys = context.ExternalApiKeys;
        if (context.Database.IsNpgsql())
        {
            string command = $$"""
                SELECT * FROM {{PostgresExternalApiKeyAuthenticationContract.FunctionSql(context)}}({0})
                """;
            keys = context.ExternalApiKeys.FromSqlRaw(command, keyId);
        }

        return await keys.IgnoreTenantFilter(TenantFilterBypassReasons.ExternalApiKeyAuthentication)
            .AsNoTracking()
            .SingleOrDefaultAsync(key => key.KeyId == keyId, cancellationToken);
    }

    internal static async Task<bool> TouchUsageAsync(
        ExploreDbContext context,
        Guid id,
        DateTime usedAtUtc,
        string? lastUsedIp,
        DateTime threshold,
        CancellationToken cancellationToken)
    {
        if (context.Database.IsNpgsql())
        {
            string command = $$"""
                SELECT {{PostgresExternalApiKeyAuthenticationContract.UsageFunctionSql(context)}}(
                    {0}, {1}, {2}, {3}) AS "Value"
                """;
            var ip = new NpgsqlParameter("last_ip", NpgsqlDbType.Text)
            {
                Value = (object?)lastUsedIp ?? DBNull.Value
            };
            return await context.Database.SqlQueryRaw<bool>(command, id, usedAtUtc, ip, threshold)
                .SingleAsync(cancellationToken);
        }

        return await context.ExternalApiKeys
            .IgnoreTenantFilter(TenantFilterBypassReasons.ExternalApiKeyAuthentication)
            .Where(key => key.Id == id && (key.LastUsedAt == null || key.LastUsedAt < threshold))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(key => key.LastUsedAt, usedAtUtc)
                .SetProperty(key => key.LastUsedIp, lastUsedIp), cancellationToken) > 0;
    }
}
