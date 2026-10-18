using Explore.Domain;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Database.ProviderPrimitives;

/// <summary>Reads only the live navigation image on the repository-owned Global context.</summary>
internal static class UserTenantNavigationQuery
{
    internal static async Task<User?> ReadAsync(
        ExploreDbContext context, IServiceProvider services, Guid userId, CancellationToken cancellationToken)
    {
        if (!context.Database.IsNpgsql())
            return await context.Users.IgnoreAutoIncludes().AsNoTracking()
                .Where(user => user.Id == userId && !user.IsDeleted)
                .Select(user => new User { Id = user.Id, LastActiveTenantId = user.LastActiveTenantId, Pii = null! })
                .SingleOrDefaultAsync(cancellationToken);

        await PostgresUserTenantNavigationContract.RequireReadyAsync(context,
            services.GetRequiredService<PostgresNativeQueueEnrollment>(), cancellationToken);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT * FROM {PostgresUserTenantNavigationContract.FunctionSql(context)}(@subject)";
        command.Parameters.Add(new NpgsqlParameter("subject", NpgsqlDbType.Uuid) { Value = userId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new User { Id = reader.GetGuid(0), LastActiveTenantId = reader.IsDBNull(1) ? null : reader.GetGuid(1), Pii = null! }
            : null;
    }
}
