using System.Data;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

/// <summary>Fixed Runtime storage for explicitly admitted external account enrollment.</summary>
public static partial class PostgresExternalTenantEnrollmentPermissions
{
    private const string Drift = "external_enrollment_permissions_drift";
    private const string NotReady = "external_enrollment_permissions_not_ready";

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        IdentityDatabaseTopology identityTopology, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        if (!Enum.IsDefined(identityTopology)) throw new ArgumentOutOfRangeException(nameof(identityTopology));
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        var transaction = context.Database.CurrentTransaction!;
        var physical = transaction.GetDbTransaction();
        var connection = RelationalConnectionIdentity.Capture(context);
        var contextId = context.ContextId;
        int? backend = connection.BackendProcessId;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change)
        {
            if (change.CurrentState != ConnectionState.Open) interrupted = true;
        }
        void RequireOriginal()
        {
            if (interrupted || context.ContextId != contextId || !connection.Matches(context)
                || connection.State != ConnectionState.Open || connection.BackendProcessId != backend
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction)
                || !ReferenceEquals(physical, context.Database.CurrentTransaction?.GetDbTransaction())
                || !connection.Owns(physical))
                throw new UnauthorizedAccessException("external_enrollment_installer_owner_changed");
        }
        async Task<bool> Read(string predicate)
        {
            RequireOriginal();
            bool result = await IsTrueAsync(context, predicate, cancellationToken);
            RequireOriginal();
            return result;
        }
        string savepoint = "external_enrollment_" + Guid.CreateVersion7().ToString("N");
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            try
            {
                if (!await Read(Identity(enrollment.MigratorLogin)))
                    throw new UnauthorizedAccessException("external_enrollment_installer_wrong_identity");
                if (!await Read(OptionalCataloguePredicate(context, enrollment)))
                    throw new InvalidOperationException(Drift);
                await PostgresManagedTenantWorkerCreationPermissions.RequireExternalEnrollmentSourcesAsync(
                    context, enrollment, identityTopology, cancellationToken);
                RequireOriginal();
                if (await Read(Absent(context, enrollment)))
                {
                    var names = context.GetService<ISqlGenerationHelper>();
                    foreach (var source in Sources(context))
                    {
                        await ExecuteAsync(context,
                            $"GRANT USAGE ON SCHEMA {names.DelimitIdentifier(source.Schema)} TO {names.DelimitIdentifier(enrollment.RuntimeLogin)}",
                            cancellationToken);
                        RequireOriginal();
                        foreach (string privilege in new[] { "SELECT", "INSERT" })
                        {
                            var columns = source.Own.Where(grant => grant.Privilege == privilege)
                                .Select(grant => names.DelimitIdentifier(grant.Column)).ToArray();
                            if (columns.Length == 0) continue;
                            await ExecuteAsync(context,
                                $"GRANT {privilege} ({string.Join(',', columns)}) ON {source.Table} TO {names.DelimitIdentifier(enrollment.RuntimeLogin)}",
                                cancellationToken);
                            RequireOriginal();
                        }
                    }
                }
                if (!await Read(Catalogue(context, enrollment, complete: true)))
                    throw new InvalidOperationException(Drift);
            }
            catch
            {
                RequireOriginal();
                await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
                throw;
            }
            finally
            {
                RequireOriginal();
                await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None);
            }
            RequireOriginal();
            cancellationToken.ThrowIfCancellationRequested();
            if (owned is not null) await owned.CommitAsync(cancellationToken);
        }
        finally { connection.StateChange -= Changed; }
    }

    /// <summary>Attests the already selected open Runtime connector and its unchanged tenant and transaction.</summary>
    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        IdentityDatabaseTopology identityTopology, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        if (enrollment is null) throw new UnauthorizedAccessException(NotReady);
        enrollment.Validate();
        if (!Enum.IsDefined(identityTopology)) throw new ArgumentOutOfRangeException(nameof(identityTopology));
        context.RequireBoundTenantLease();
        var connection = RelationalConnectionIdentity.Capture(context);
        var physical = context.Database.CurrentTransaction?.GetDbTransaction();
        var contextId = context.ContextId;
        var tenantContext = context.TenantContext;
        Guid? tenant = context.TenantFilterTenantId;
        int? backend = connection.State == ConnectionState.Open ? connection.BackendProcessId : null;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change)
        {
            if (change.CurrentState != ConnectionState.Open) interrupted = true;
        }
        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.RequireBoundTenantLease();
            if (tenant is null || tenant == Guid.Empty || context.TenantFilterTenantId != tenant
                || !ReferenceEquals(tenantContext, context.TenantContext) || context.IsTenantFilterBypassed
                || context.IsConfigurationInstanceImportOwner || System.Transactions.Transaction.Current is not null
                || interrupted || context.ContextId != contextId || !connection.Matches(context)
                || connection.State != ConnectionState.Open || connection.BackendProcessId != backend
                || !ReferenceEquals(physical, context.Database.CurrentTransaction?.GetDbTransaction())
                || physical is not null && !connection.Owns(physical))
                throw new UnauthorizedAccessException(NotReady);
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            if (!await IsTrueAsync(context, $"({Identity(enrollment.RuntimeLogin)}) AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')={Text(tenant!.Value.ToString("D"))}", cancellationToken))
                throw new UnauthorizedAccessException(NotReady);
            RequireOriginal();
            if (!await IsTrueAsync(context, Catalogue(context, enrollment, complete: true), cancellationToken))
                throw new UnauthorizedAccessException(NotReady);
            RequireOriginal();
            await PostgresManagedTenantWorkerCreationPermissions.RequireExternalEnrollmentSourcesAsync(
                context, enrollment, identityTopology, cancellationToken);
            RequireOriginal();
        }
        finally { connection.StateChange -= Changed; }
    }

    private static string Identity(string login) =>
        $"session_user={Text(login)} AND current_user=session_user AND pg_catalog.current_setting('role') IN ('none',session_user::text) AND pg_catalog.current_setting('session_replication_role')='origin'";

    private static string Role(string login) => $"(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(login)})";
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";

    private static async Task<bool> IsTrueAsync(ExploreDbContext context, string predicate, CancellationToken token)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? 30;
        command.CommandText = $"SELECT ({predicate})";
        return await command.ExecuteScalarAsync(token) is true;
    }

    private static async Task ExecuteAsync(ExploreDbContext context, string sql, CancellationToken token)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? 30;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(token);
    }
}
