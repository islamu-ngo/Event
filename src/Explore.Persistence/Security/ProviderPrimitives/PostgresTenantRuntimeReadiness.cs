using System.Data.Common;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Explore.Persistence.Security;

/// <summary>
/// Inspects database authority and model-owned policy coverage without provisioning privileges or policies.
/// </summary>
internal static class PostgresTenantRuntimeReadiness
{
    internal static DbCommand CreateCommand(
        DbConnection connection, ExploreDbContext context, string? enrolledGlobalLogin = null)
    {
        string globalPolicyException = enrolledGlobalLogin is null
            ? "FALSE"
            : PostgresEmailRetentionReadPermissions.GlobalPolicyException(context, enrolledGlobalLogin);
        var command = connection.CreateCommand();
        command.CommandTimeout = 5;
        command.CommandText = $$"""
            SELECT
                NOT EXISTS (
                    SELECT 1 FROM pg_roles r
                    WHERE (pg_has_role(current_user, r.oid, 'SET') OR pg_has_role(current_user, r.oid, 'USAGE'))
                      AND (r.rolsuper OR r.rolbypassrls OR r.rolcreaterole OR r.rolcreatedb OR r.rolreplication))
                AND NOT EXISTS (
                    SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = ANY(@schemas) AND c.relkind IN ('r', 'p', 'v', 'm', 'f')
                      AND (pg_has_role(current_user, c.relowner, 'SET') OR pg_has_role(current_user, c.relowner, 'USAGE')
                           OR has_table_privilege(current_user, c.oid, 'TRUNCATE')))
                AND NOT EXISTS (
                    SELECT 1 FROM pg_namespace n WHERE n.nspname = ANY(@schemas)
                    AND has_schema_privilege(current_user, n.oid, 'CREATE'))
                AND NOT EXISTS (
                    SELECT 1 FROM unnest(@schemas, @tables, @fingerprints)
                        AS expected(schema_name, table_name, fingerprint)
                    LEFT JOIN pg_namespace n ON n.nspname = expected.schema_name
                    LEFT JOIN pg_class c ON c.relnamespace = n.oid AND c.relname = expected.table_name
                    WHERE c.oid IS NULL OR NOT c.relrowsecurity OR NOT c.relforcerowsecurity
                        OR NOT EXISTS (
                            SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid
                              AND p.polname = 'tenant_isolation' AND p.polcmd = '*'
                              AND p.polroles = ARRAY[0::oid] AND p.polpermissive
                              AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                              AND obj_description(p.oid, 'pg_policy') =
                                  expected.fingerprint || md5(p.polqual::text)
                                      || ':' || md5(p.polwithcheck::text)))
                AND NOT EXISTS (
                    SELECT 1 FROM pg_policy p JOIN pg_class c ON c.oid = p.polrelid
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = ANY(@schemas) AND c.relname = ANY(@tables)
                      AND p.polname <> 'tenant_isolation' AND p.polpermissive
                      AND NOT COALESCE(({{globalPolicyException}}), FALSE)
                      AND EXISTS (
                          SELECT 1 FROM unnest(p.polroles) AS role_id
                          WHERE role_id = 0 OR pg_has_role(current_user, role_id, 'USAGE')))
            """;
        string? defaultSchema = context.GetService<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptions>()
            .FindExtension<Explore.Persistence.Database.RelationalNamespaceOptionsExtension>()?.TargetSchema;
        var tables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        var schemas = command.CreateParameter();
        schemas.ParameterName = "schemas";
        schemas.Value = tables.Select(table => table.Schema ?? defaultSchema ?? "public").ToArray();
        command.Parameters.Add(schemas);
        var names = command.CreateParameter();
        names.ParameterName = "tables";
        names.Value = tables.Select(table => table.TableName).ToArray();
        command.Parameters.Add(names);
        var fingerprints = command.CreateParameter();
        fingerprints.ParameterName = "fingerprints";
        fingerprints.Value = tables.Select(table =>
            PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(table, defaultSchema)).ToArray();
        command.Parameters.Add(fingerprints);
        PostgresMachineMutationAuthorityContract.AddReadiness(command, context, required: false);
        PostgresMachineRoomMutationAuthorityContract.AddReadiness(command, context, required: false);
        return command;
    }

    internal static void RequireReady(object? result)
    {
        if (result is not true)
            throw new InvalidOperationException("postgres_tenant_runtime_authority_not_ready");
    }
}
