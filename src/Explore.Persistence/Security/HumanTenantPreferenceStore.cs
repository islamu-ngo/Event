using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Security;

internal sealed class HumanTenantPreferenceStore(
    ExploreDbContext context,
    ExploreDbContextExecutionLease lease,
    IHumanTenantPreferencePreparation preparation,
    IServiceProvider services) : IHumanTenantPreferenceStore
{
    private IHumanTenantPreferenceMutation? _completed;
    private int _used;

    public async Task<bool> WriteAsync(IHumanTenantPreferenceMutation mutation, CancellationToken token)
    {
        RequireOwner(mutation);
        if (Interlocked.Exchange(ref _used, 1) != 0)
            throw new InvalidOperationException("A preference writer permits one original effect.");
        await mutation.RequireCurrentAsync(token);
        bool written;
        if (context.Database.IsNpgsql())
        {
            await PostgresTenantPreferenceContract.RequireReadyAsync(context,
                services.GetRequiredService<PostgresNativeQueueEnrollment>(), token);
            written = await ExecuteNativeAsync(PostgresTenantPreferenceContract.FunctionSql(context), mutation, token);
        }
        else
        {
            written = await context.Users.Where(user => user.Id == mutation.SubjectId && !user.IsDeleted)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(user => user.LastActiveTenantId, mutation.TenantId)
                    .SetProperty(user => user.UpdatedAt, mutation.UpdatedAt)
                    .SetProperty(user => user.UpdatedBy, mutation.SubjectId), token) == 1;
        }
        if (!written) return false;
        _completed = mutation;
        await RequireCompleteAsync(mutation, token);
        return true;
    }

    public async Task RequireCompleteAsync(IHumanTenantPreferenceMutation mutation, CancellationToken token)
    {
        RequireOwner(mutation);
        if (!ReferenceEquals(_completed, mutation))
            throw new UnauthorizedAccessException("The original preference effect is incomplete.");
        bool matches;
        if (context.Database.IsNpgsql())
        {
            await PostgresTenantPreferenceContract.RequireReadyAsync(context,
                services.GetRequiredService<PostgresNativeQueueEnrollment>(), token);
            matches = await ExecuteNativeAsync(PostgresTenantPreferenceContract.ImageFunctionSql(context), mutation, token);
        }
        else
        {
            matches = await context.Users.AsNoTracking().AnyAsync(user => user.Id == mutation.SubjectId
                && !user.IsDeleted && user.LastActiveTenantId == mutation.TenantId
                && user.UpdatedAt == mutation.UpdatedAt && user.UpdatedBy == mutation.SubjectId, token);
        }
        if (!matches) throw new UnauthorizedAccessException("The original preference image changed before settlement.");
    }

    private void RequireOwner(IHumanTenantPreferenceMutation mutation)
    {
        lease.RequireOriginalOwner(context);
        var human = lease.HumanScope
            ?? throw new UnauthorizedAccessException("Preference data requires the original human scope.");
        if (!ReferenceEquals(preparation.CurrentMutation, mutation)
            || human.Subject.PrincipalId != mutation.SubjectId
            || human.Purpose != HumanAdministratorCommitPurpose.GlobalInstance
            || lease.SelectedTenantId is not null || lease.PolicyPurpose != PolicyMutationPurpose.Standard
            || context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || context.ChangeTracker.HasChanges())
            throw new UnauthorizedAccessException("Preference data requires the original prepared Serializable owner.");
    }

    private async Task<bool> ExecuteNativeAsync(string function, IHumanTenantPreferenceMutation mutation, CancellationToken token)
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
