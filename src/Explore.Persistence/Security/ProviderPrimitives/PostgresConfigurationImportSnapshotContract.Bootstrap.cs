using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

public static partial class PostgresConfigurationImportSnapshotContract
{
    private static readonly string[] BootstrapColumns =
    [
        "Id", "Mode", "ApiVersion", "Kind", "ManifestName", "Digest", "Status",
        "RequestedTenantCount", "CreatedTenantCount", "SkippedExistingTenantCount",
        "FailedTenantCount", "ReasonCode", "Reason", "StartedAt", "CompletedAt",
        "InstanceSectionDigest", "BootstrapGeneration",
        "_instanceChangedSettingKeyNames", "_instanceChangedDocumentKeyNames", "EffectOutboxId"
    ];

    public static async Task<ConfigurationManifestOperation?> ReadLatestAppliedBootstrapAsync(
        ExploreDbContext context, CancellationToken cancellationToken)
    {
        var operations = await ReadAsync(context, "bootstrap", null, null, reader =>
        {
            var operation = ConfigurationManifestOperation.Create(
                reader.GetGuid(0), Enum.Parse<ConfigurationManifestAuditMode>(reader.GetString(1)),
                reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
                Enum.Parse<ConfigurationManifestOperationStatus>(reader.GetString(6)),
                reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.GetDateTime(13), reader.GetDateTime(14),
                reader.GetString(15), reader.GetInt32(16),
                reader.GetString(17).Split('\n', StringSplitOptions.RemoveEmptyEntries),
                reader.GetString(18).Split('\n', StringSplitOptions.RemoveEmptyEntries));
            if (NullableGuid(reader, 19) is { } effect)
                operation.BindEffectOutbox(effect);
            return operation;
        }, cancellationToken, "\n" + BuildBootstrapReadBody(context) + "\n");
        return operations.SingleOrDefault();
    }

    private static string BootstrapTable(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            context.Model.FindEntityType(typeof(ConfigurationManifestOperation))!.GetTableName()!,
            Schema(context, typeof(ConfigurationManifestOperation)));

    private static string BootstrapColumn(ExploreDbContext context, string property)
    {
        var entity = context.Model.FindEntityType(typeof(ConfigurationManifestOperation))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
    }

    private static string BootstrapFunctionAuthorityPredicate() => $"""
        procedure.proowner = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')
        AND procedure.prosecdef AND procedure.proretset AND procedure.provolatile = 's'
        AND procedure.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
        AND procedure.proconfig = ARRAY['search_path=pg_catalog']::text[]
        AND NOT EXISTS (
            SELECT 1 FROM pg_catalog.aclexplode(COALESCE(procedure.proacl,
                pg_catalog.acldefault('f', procedure.proowner))) permission
            WHERE permission.grantee NOT IN (
                SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ('{OwnerRole}', '{GlobalRole}'))
                OR permission.grantee = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{GlobalRole}')
                    AND permission.is_grantable)
        AND EXISTS (
            SELECT 1 FROM pg_catalog.aclexplode(COALESCE(procedure.proacl,
                pg_catalog.acldefault('f', procedure.proowner))) permission
            WHERE permission.grantee = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{GlobalRole}')
                AND permission.privilege_type = 'EXECUTE' AND NOT permission.is_grantable)
        """;

    private static string BuildBootstrapReadBody(ExploreDbContext context)
    {
        string C(string property) => BootstrapColumn(context, property);
        string table = BootstrapTable(context);
        return $"""
            BEGIN
                IF p_tenant IS NOT NULL OR current_user <> '{OwnerRole}'
                    OR current_setting('role') <> 'none'
                    OR NOT pg_catalog.pg_has_role(session_user, '{GlobalRole}', 'USAGE')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN
                        ('{OwnerRole}', '{MigratorRole}', '{GlobalRole}')
                        AND (rolcanlogin OR rolsuper OR rolbypassrls OR rolcreatedb
                            OR rolcreaterole OR rolreplication OR rolinherit))
                    OR pg_catalog.pg_has_role(session_user, '{OwnerRole}', 'MEMBER')
                    OR pg_catalog.pg_has_role(session_user, '{MigratorRole}', 'MEMBER')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members edge
                        JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                        JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                        WHERE member.rolname IN ('{OwnerRole}', '{GlobalRole}')
                            OR member.rolname = '{MigratorRole}' AND parent.rolname <> '{OwnerRole}'
                            OR parent.rolname = '{OwnerRole}' AND member.rolname <> '{MigratorRole}')
                    OR has_table_privilege('{OwnerRole}', {Text(table)}::regclass,
                        'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    OR has_any_column_privilege('{OwnerRole}', {Text(table)}::regclass,
                        'INSERT,UPDATE,REFERENCES')
                    OR has_table_privilege('{GlobalRole}', {Text(table)}::regclass,
                        'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    OR has_any_column_privilege('{GlobalRole}', {Text(table)}::regclass,
                        'SELECT,INSERT,UPDATE,REFERENCES')
                THEN RAISE EXCEPTION 'configuration_import_bootstrap_source_not_ready' USING ERRCODE = '42501';
                END IF;
                RETURN QUERY SELECT {string.Join(", ", BootstrapColumns.Select(property => "operation." + C(property)))}
                FROM {table} operation
                WHERE operation.{C("Status")} = '{nameof(ConfigurationManifestOperationStatus.Applied)}'
                    AND operation.{C("InstanceSectionDigest")} IS NOT NULL
                    AND operation.{C("BootstrapGeneration")} IS NOT NULL
                ORDER BY operation.{C("BootstrapGeneration")} DESC,
                    operation.{C("CompletedAt")} DESC, operation.{C("Id")} DESC
                LIMIT 1;
            END;
            """;
    }

    private static string BuildBootstrapReadInstallSql(ExploreDbContext context) => $"""
        GRANT USAGE ON SCHEMA {context.GetService<ISqlGenerationHelper>()
            .DelimitIdentifier(Schema(context, typeof(ConfigurationManifestOperation)))} TO {OwnerRole};
        DO $bootstrap_grants$
        BEGIN
            IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc procedure
                    WHERE procedure.oid = to_regprocedure({Text(FunctionSql(context, "bootstrap") + "(uuid)")})
                        AND (procedure.prosrc IS DISTINCT FROM {Text("\n" + BuildBootstrapReadBody(context) + "\n")}
                            OR NOT COALESCE(({BootstrapFunctionAuthorityPredicate()}), false)))
                OR has_table_privilege('{OwnerRole}', {Text(BootstrapTable(context))}::regclass,
                    'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                OR has_any_column_privilege('{OwnerRole}', {Text(BootstrapTable(context))}::regclass,
                    'INSERT,UPDATE,REFERENCES')
                OR has_table_privilege('{GlobalRole}', {Text(BootstrapTable(context))}::regclass,
                    'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                OR has_any_column_privilege('{GlobalRole}', {Text(BootstrapTable(context))}::regclass,
                    'SELECT,INSERT,UPDATE,REFERENCES')
            THEN RAISE EXCEPTION 'configuration_import_bootstrap_privilege_drift' USING ERRCODE = '55000';
            END IF;
        END; $bootstrap_grants$;
        GRANT SELECT ({string.Join(", ", BootstrapColumns.Select(property => BootstrapColumn(context, property)))})
            ON {BootstrapTable(context)} TO {OwnerRole};
        """;
}
