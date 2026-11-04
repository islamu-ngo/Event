using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database.ProviderPrimitives;

/// <summary>Reads PostgreSQL session state through the caller's original transaction; it grants no authority.</summary>
internal static class PostgresTenantSessionProbe
{
    internal static async Task<bool> MatchesAsync(
        ExploreDbContext context, IDbContextTransaction? transaction, Guid? tenantId, CancellationToken cancellationToken)
    {
        if (context.Database.ProviderName != RelationalNamedLock.PostgreSqlProvider) return true;

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction?.GetDbTransaction();
        command.CommandText = "SELECT current_setting('app.current_tenant_id', true)";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return tenantId is null
            ? value is null or DBNull || value is string { Length: 0 }
            : value is string setting && Guid.TryParse(setting, out var current) && current == tenantId;
    }
}
