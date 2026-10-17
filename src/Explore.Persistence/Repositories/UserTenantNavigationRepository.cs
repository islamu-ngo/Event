using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Repositories;

/// <summary>Separate Global read of a resolved user's live navigation preference; never loads identity data.</summary>
public sealed class UserTenantNavigationRepository(
    IGlobalExploreDbContextFactory globalFactory,
    IServiceProvider services) : IUserTenantNavigationRepository
{
    public async Task<User?> GetNavigationUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
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
