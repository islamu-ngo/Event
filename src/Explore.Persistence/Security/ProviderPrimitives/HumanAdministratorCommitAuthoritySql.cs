using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>Calls only fixed Boolean fences on the supplied owner's connection and transaction.</summary>
internal static class HumanAdministratorCommitAuthoritySql
{
    internal static async Task<bool?> TryFenceNativeAsync(
        ExploreDbContext context, HumanAdministratorCommitScope scope, bool tenant,
        CancellationToken cancellationToken, bool subjectOnly = false) =>
        context.Database.IsNpgsql()
            ? await FenceAsync(context, scope, tenant, cancellationToken, subjectOnly)
            : null;

    internal static async Task<bool> FenceAsync(
        ExploreDbContext context, HumanAdministratorCommitScope scope, bool tenant,
        CancellationToken cancellationToken, bool subjectOnly = false)
    {
        scope.RequireOwner(context);
        var enrollment = scope.Enrollment
            ?? throw new InvalidOperationException("human_administrator_native_enrollment_missing");
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = PostgresHumanAdministratorCommitAuthorityContract.AdmissionSql(context, enrollment);
        await command.ExecuteNonQueryAsync(cancellationToken);
        command.CommandText = $"SELECT {(subjectOnly
            ? PostgresHumanAdministratorCommitAuthorityContract.SubjectFunctionSql(context)
            : tenant
            ? PostgresHumanAdministratorCommitAuthorityContract.TenantFunctionSql(context)
            : PostgresHumanAdministratorCommitAuthorityContract.InstanceFunctionSql(context))}"
            + "(@actor, @provider, @account, @purpose, @tenant)";
        command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Uuid) { Value = scope.Subject.PrincipalId });
        command.Parameters.Add(new NpgsqlParameter("provider", NpgsqlDbType.Integer)
        { Value = scope.Subject.AccountKey is { } account ? (int)account.ProviderKind : DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("account", NpgsqlDbType.Text)
        { Value = (object?)scope.Subject.AccountKey?.Value ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("purpose", NpgsqlDbType.Integer) { Value = (int)scope.Purpose });
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid)
        { Value = (object?)scope.TenantId ?? DBNull.Value });
        bool allowed = await command.ExecuteScalarAsync(cancellationToken) is true;
        scope.RequireOwner(context);
        command.Parameters.Clear();
        command.CommandText = PostgresHumanAdministratorCommitAuthorityContract.AdmissionSql(context, enrollment);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return allowed;
    }
}
