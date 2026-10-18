using Explore.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>Executes the fixed preference write and image queries on the original store-owned context.</summary>
internal static class HumanTenantPreferenceDataOperations
{
    internal static async Task<bool> WriteAsync(
        ExploreDbContext context, IServiceProvider services, IHumanTenantPreferenceMutation mutation, CancellationToken token)
    {
        if (context.Database.IsNpgsql())
        {
            await PostgresTenantPreferenceContract.RequireReadyAsync(context,
                services.GetRequiredService<PostgresNativeQueueEnrollment>(), token);
            return await ExecuteNativeAsync(context, PostgresTenantPreferenceContract.FunctionSql(context), mutation, token);
        }

        return await context.Users.Where(user => user.Id == mutation.SubjectId && !user.IsDeleted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(user => user.LastActiveTenantId, mutation.TenantId)
                .SetProperty(user => user.UpdatedAt, mutation.UpdatedAt)
                .SetProperty(user => user.UpdatedBy, mutation.SubjectId), token) == 1;
    }

    internal static async Task<bool> MatchesAsync(
        ExploreDbContext context, IServiceProvider services, IHumanTenantPreferenceMutation mutation, CancellationToken token)
    {
        if (context.Database.IsNpgsql())
        {
            await PostgresTenantPreferenceContract.RequireReadyAsync(context,
                services.GetRequiredService<PostgresNativeQueueEnrollment>(), token);
            return await ExecuteNativeAsync(context, PostgresTenantPreferenceContract.ImageFunctionSql(context), mutation, token);
        }

        return await context.Users.AsNoTracking().AnyAsync(user => user.Id == mutation.SubjectId
            && !user.IsDeleted && user.LastActiveTenantId == mutation.TenantId
            && user.UpdatedAt == mutation.UpdatedAt && user.UpdatedBy == mutation.SubjectId, token);
    }

    private static async Task<bool> ExecuteNativeAsync(
        ExploreDbContext context, string function, IHumanTenantPreferenceMutation mutation, CancellationToken token)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"SELECT {function}(@subject, @tenant, @updated_at)";
        command.Parameters.Add(new NpgsqlParameter("subject", NpgsqlDbType.Uuid) { Value = mutation.SubjectId });
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = mutation.TenantId });
        command.Parameters.Add(new NpgsqlParameter("updated_at", NpgsqlDbType.TimestampTz) { Value = mutation.UpdatedAt });
        return await command.ExecuteScalarAsync(token) is true;
    }
}
