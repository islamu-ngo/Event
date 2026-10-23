using System.Data;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Explore.Persistence.Schema;

/// <summary>Directory read admission without registration-private authority or business routines.</summary>
internal static class PostgresRegistrationRetentionReadPermissions
{
    internal static async Task RequireReadyAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment? enrollment, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        if (enrollment is null) throw new UnauthorizedAccessException("registration_retention_enrollment_required");
        enrollment.Validate();
        var connection = RelationalConnectionIdentity.Capture(context);
        var contextId = context.ContextId;
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        int? backend = connection.BackendProcessId;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change)
        {
            if (change.CurrentState != ConnectionState.Open) interrupted = true;
        }
        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.RequireBoundTenantLease();
            if (context.ContextId != contextId || context.TenantFilterTenantId is not null
                || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
                || interrupted || !connection.Matches(context) || connection.State != ConnectionState.Open
                || connection.BackendProcessId != backend
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction())
                || transaction is not null && !connection.Owns(transaction))
                throw new UnauthorizedAccessException("registration_retention_original_owner_changed");
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            await PostgresUserTenantMembershipReadPermissions.RequireReadyAsync(context, enrollment, cancellationToken);
            RequireOriginal();
            var names = new HumanPrincipalSourceSql(context);
            Type[] privateSources = [typeof(RegistrationAnswer), typeof(RegistrationSensitiveAnswerValue),
                typeof(RegistrationOrderPii), typeof(RegistrationParticipantPii)];
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT NOT EXISTS (
                    SELECT 1 FROM unnest(@relations::text[]) source(relation)
                    LEFT JOIN pg_catalog.pg_class t ON t.oid=pg_catalog.to_regclass(source.relation)
                    WHERE t.oid IS NULL OR t.relkind<>'r' OR NOT t.relrowsecurity OR NOT t.relforcerowsecurity
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                            WHERE pg_catalog.pg_has_role((SELECT oid FROM pg_catalog.pg_roles WHERE rolname=@global),r.oid,'MEMBER')
                                AND (r.oid=t.relowner
                                    OR pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                                        CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                                        WHERE a.attrelid=t.oid AND a.attnum<>0 AND NOT a.attisdropped
                                            AND (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege)
                                                OR pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege || ' WITH GRANT OPTION'))))))
                    AND pg_catalog.to_regprocedure(@routine) IS NULL
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN
                        ('registration_retention_discovery_owner','registration_retention_discovery_migrator',
                         'registration_retention_discovery_executor'))
                """;
            command.Parameters.Add(new NpgsqlParameter("relations", privateSources.Select(names.Table).ToArray()));
            command.Parameters.Add(new NpgsqlParameter("global", enrollment.GlobalLogin));
            command.Parameters.Add(new NpgsqlParameter("routine",
                names.Name(context.Model.FindEntityType(typeof(Tenant))!.GetTableName()! + "_registration_retention_ids") + "(uuid,integer)"));
            bool ready = await command.ExecuteScalarAsync(cancellationToken) is true;
            RequireOriginal();
            if (!ready) throw new UnauthorizedAccessException("registration_retention_read_permissions_not_ready");
        }
        finally { connection.StateChange -= Changed; }
    }
}
